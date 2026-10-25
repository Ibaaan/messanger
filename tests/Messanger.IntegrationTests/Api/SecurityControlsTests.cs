using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Messanger.Application.Authentication;
using Messanger.Application.Messages;
using Messanger.Infrastructure.Persistence;
using Messanger.IntegrationTests.Support;
using Messanger.Security;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Messanger.IntegrationTests.Api;

internal static class SecurityControlsTests
{
    public static async Task RegistrationLimitsUseTheRealPeerAndIgnoreUntrustedForwarders(ApiTestHost host)
    {
        await using var limited = await host.CreateSecurityHostAsync(options => { Relax(options); options.RegistrationPerIp = 2; });
        for (var i = 0; i < 2; i++)
        {
            using var accepted = await SendAsync(limited.Client, HttpMethod.Post, "/registration", "198.51.100.10",
                new { username = "register_" + i, password = ApiTestHost.TestPassword });
            TestAssert.Equal(HttpStatusCode.Created, accepted.StatusCode, "Allow attempts within the IP quota.");
        }
        using var blocked = await SendAsync(limited.Client, HttpMethod.Post, "/REGISTRATION/", "::ffff:198.51.100.10",
            new { username = "register_blocked", password = ApiTestHost.TestPassword }, forwarded: "203.0.113.99");
        await LimitedAsync(blocked);
        using var other = await SendAsync(limited.Client, HttpMethod.Post, "/registration", "198.51.100.11",
            new { username = "register_other", password = ApiTestHost.TestPassword });
        TestAssert.Equal(HttpStatusCode.Created, other.StatusCode, "An unrelated peer has its own allowance.");
        await using var scope = limited.Services.CreateAsyncScope();
        TestAssert.True(!await scope.ServiceProvider.GetRequiredService<MessangerDbContext>().Users.AnyAsync(user => user.Username == "register_blocked"), "Reject before executing registration.");
    }

    public static async Task LoginLimitsBothIpAndNormalizedAccountWithoutEnumeratingUsers(ApiTestHost host)
    {
        await using (var limited = await host.CreateSecurityHostAsync(options => { Relax(options); options.LoginPerIp = 2; }))
        {
            for (var i = 0; i < 2; i++)
            {
                using var failed = await LoginAttemptAsync(limited, "unknown_" + i, "198.51.100.20");
                await ApiAssertions.ErrorAsync(failed, HttpStatusCode.Unauthorized, "invalid_credentials");
            }
            using var rejected = await LoginAttemptAsync(limited, "unknown_more", "198.51.100.20");
            await LimitedAsync(rejected);
        }
        await using var accounts = await host.CreateSecurityHostAsync(options => { Relax(options); options.LoginPerAccount = 2; });
        await accounts.RegisterAsync("limit_alice");
        foreach (var name in new[] { "limit_alice", "unknown_user" })
        {
            using var first = await LoginAttemptAsync(accounts, name, "198.51.100.21");
            using var second = await LoginAttemptAsync(accounts, name.ToUpperInvariant(), "198.51.100.22");
            await ApiAssertions.ErrorAsync(first, HttpStatusCode.Unauthorized, "invalid_credentials");
            await ApiAssertions.ErrorAsync(second, HttpStatusCode.Unauthorized, "invalid_credentials");
            using var rejected = await LoginAttemptAsync(accounts, name, "198.51.100.23");
            await LimitedAsync(rejected);
        }
    }

    public static async Task SessionThrottlingDoesNotRotateOrClearTheRefreshCookie(ApiTestHost host)
    {
        await using var limited = await host.CreateSecurityHostAsync(options => { Relax(options); options.SessionsPerIp = 2; });
        var user = await limited.RegisterAsync();
        using var browser = await SessionBrowser.CreateAsync(limited);
        using var login = await browser.Client.PostAsJsonAsync("/auth/login", new { username = user.Username, password = ApiTestHost.TestPassword });
        login.EnsureSuccessStatusCode();
        browser.Client.DefaultRequestHeaders.Add("X-Test-Peer-IP", "198.51.100.30");
        for (var i = 0; i < 2; i++) { using var csrf = await browser.Client.GetAsync("/auth/csrf"); csrf.EnsureSuccessStatusCode(); }
        var original = browser.Cookies.GetCookieHeader(new Uri("https://localhost/auth/refresh"));
        using var blocked = await browser.Client.PostAsync("/auth/refresh", null);
        await LimitedAsync(blocked);
        TestAssert.Equal(original, browser.Cookies.GetCookieHeader(new Uri("https://localhost/auth/refresh")), "A 429 must preserve the valid cookie for a later retry.");
        using var active = await limited.GetCurrentUserAsync((await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        active.EnsureSuccessStatusCode();
    }

    public static async Task PasswordConfirmationBudgetIsSharedAcrossPeerAddresses(ApiTestHost host)
    {
        await using var limited = await host.CreateSecurityHostAsync(options => { Relax(options); options.PasswordChangesPerUser = 2; });
        var user = await limited.RegisterAsync();
        var token = (await limited.LoginAsync(user)).AccessToken;
        for (var i = 0; i < 2; i++)
        {
            using var failed = await SendAsync(limited.Client, HttpMethod.Post, "/api/users/me/password", "198.51.100." + (40 + i),
                new { currentPassword = "incorrect password", newPassword = "new passphrase 123" }, token);
            await ApiAssertions.ErrorAsync(failed, HttpStatusCode.Unauthorized, "invalid_credentials");
        }
        using var blocked = await SendAsync(limited.Client, HttpMethod.Post, "/api/users/me/password", "198.51.100.42",
            new { currentPassword = ApiTestHost.TestPassword, newPassword = "new passphrase 123" }, token);
        await LimitedAsync(blocked);
        using var current = await limited.GetCurrentUserAsync(token);
        current.EnsureSuccessStatusCode();
    }

    public static async Task MessageBudgetsCoverRepliesAndAllUserConnections(ApiTestHost host)
    {
        await using var limited = await host.CreateSecurityHostAsync(options => { Relax(options); options.MessagesPerUser = 2; });
        var group = await ChatTestGroup.CreateAsync(limited);
        await using var first = await SignalRTestClient.ConnectAsync(limited, group.OwnerToken);
        await using var second = await SignalRTestClient.ConnectAsync(limited, group.OwnerToken);
        var sent = await first.Connection.InvokeAsync<MessageResponse>("SendMessage", group.Chat.Id, "First message");
        await second.Connection.InvokeAsync("SendReply", group.Chat.Id, "Second message", sent.Id);
        await RateErrorAsync(() => first.Connection.InvokeAsync("SendMessage", group.Chat.Id, "Must not save"));
        await using var member = await SignalRTestClient.ConnectAsync(limited, group.MemberToken);
        await member.Connection.InvokeAsync("SendMessage", group.Chat.Id, "Independent user");
        await using var scope = limited.Services.CreateAsyncScope();
        TestAssert.Equal(3, await scope.ServiceProvider.GetRequiredService<MessangerDbContext>().Messages.CountAsync(message => message.ChatId == group.Chat.Id), "Rejected sends cannot persist a message or bypass the quota through another tab.");
        TestAssert.Equal(HubConnectionState.Connected, first.Connection.State, "A message-only rejection still permits read/recovery calls.");
    }

    public static async Task ExcessiveHubCallsCloseTheAbusiveConnection(ApiTestHost host)
    {
        var logs = new SecurityLogCollector();
        await using var limited = await host.CreateSecurityHostAsync(options => { Relax(options); options.HubCallsPerUser = 1; }, logs);
        var group = await ChatTestGroup.CreateAsync(limited);
        await using var client = await SignalRTestClient.ConnectAsync(limited, group.OwnerToken);
        await client.Connection.InvokeAsync("JoinChat", group.Chat.Id);
        try { await client.Connection.InvokeAsync("LeaveChat", group.Chat.Id); }
        catch (Exception exception) when (exception is HubException or OperationCanceledException or IOException or InvalidOperationException) { }
        await client.Disconnected.WaitAsync(TimeSpan.FromSeconds(10));
        TestAssert.True(logs.Messages.Any(message => message.Contains("hub_calls")), "Record the quota closure without invocation arguments.");
        await using var member = await SignalRTestClient.ConnectAsync(limited, group.MemberToken);
        await member.Connection.InvokeAsync("JoinChat", group.Chat.Id);
    }

    public static async Task ConnectionQuotasProtectExistingClientsAndReleaseSlots(ApiTestHost host)
    {
        var logs = new SecurityLogCollector();
        await using var limited = await host.CreateSecurityHostAsync(options => { Relax(options); options.MaximumConnectionsPerUser = 1; options.MaximumConnections = 2; }, logs);
        var group = await ChatTestGroup.CreateAsync(limited);
        await using var owner = await SignalRTestClient.ConnectAsync(limited, group.OwnerToken);
        await RejectConnectionAsync(limited, group.OwnerToken);
        await using var member = await SignalRTestClient.ConnectAsync(limited, group.MemberToken);
        await RejectConnectionAsync(limited, group.OtherToken);
        await owner.Connection.InvokeAsync("JoinChat", group.Chat.Id);
        await member.Connection.InvokeAsync("JoinChat", group.Chat.Id);
        await owner.Connection.InvokeAsync("SendMessage", group.Chat.Id, "Existing clients still work");
        await member.NextMessageAsync();
        await owner.Connection.StopAsync();
        await member.Connection.InvokeAsync("LeaveChat", group.Chat.Id);
        await using var replacement = await SignalRTestClient.ConnectAsync(limited, group.OtherToken);
        await replacement.Connection.InvokeAsync("LeaveChat", group.Chat.Id);
        TestAssert.True(logs.Messages.Any(message => message.Contains("connection_rejected")), "Log quota rejection with safe metadata.");
    }

    public static async Task ReconnectingDoesNotResetConnectionAttemptBudgets(ApiTestHost host)
    {
        await using var limited = await host.CreateSecurityHostAsync(options => { Relax(options); options.ConnectionAttemptsPerUser = 1; });
        var group = await ChatTestGroup.CreateAsync(limited);
        await using (var first = await SignalRTestClient.ConnectAsync(limited, group.OwnerToken)) await first.Connection.StopAsync();
        await RejectConnectionAsync(limited, group.OwnerToken);
        await using var member = await SignalRTestClient.ConnectAsync(limited, group.MemberToken);
        await member.Connection.InvokeAsync("LeaveChat", group.Chat.Id);
    }

    public static async Task GlobalBudgetBoundsRequestsAcrossDifferentPeers(ApiTestHost host)
    {
        await using var limited = await host.CreateSecurityHostAsync(options => { Relax(options); options.RequestsPerServer = 3; });
        // The fixture's CSRF bootstrap consumed the first permit.
        for (var i = 0; i < 2; i++) { using var response = await SendAsync(limited.Client, HttpMethod.Get, "/auth/csrf", "198.51.100." + (60 + i)); response.EnsureSuccessStatusCode(); }
        using var rejected = await SendAsync(limited.Client, HttpMethod.Get, "/auth/csrf", "198.51.100.62");
        await LimitedAsync(rejected);
    }

    public static async Task LimitsRecoverAfterTheirWindowWithoutRestarting(ApiTestHost host)
    {
        await using var limited = await host.CreateSecurityHostAsync(options => { Relax(options); options.Window = TimeSpan.FromSeconds(1); options.SessionsPerIp = 1; });
        using var accepted = await SendAsync(limited.Client, HttpMethod.Get, "/auth/csrf", "198.51.100.70");
        accepted.EnsureSuccessStatusCode();
        using var rejected = await SendAsync(limited.Client, HttpMethod.Get, "/auth/csrf", "198.51.100.70");
        await LimitedAsync(rejected);
        await Task.Delay(TimeSpan.FromMilliseconds(1300));
        using var recovered = await SendAsync(limited.Client, HttpMethod.Get, "/auth/csrf", "198.51.100.70");
        recovered.EnsureSuccessStatusCode();
    }

    public static async Task CredentialConcurrencyRejectsBeforePasswordHashing(ApiTestHost host)
    {
        using var blocking = new BlockingPasswordService();
        await using var limited = await host.CreateSecurityHostAsync(options => { Relax(options); options.CredentialConcurrency = 1; }, overrides: services =>
        {
            services.RemoveAll<IPasswordService>();
            services.AddSingleton<IPasswordService>(blocking);
        });
        var first = SendAsync(limited.Client, HttpMethod.Post, "/registration", "198.51.100.80", new { username = "busy_first", password = ApiTestHost.TestPassword });
        try
        {
            await blocking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var rejected = await SendAsync(limited.Client, HttpMethod.Post, "/registration", "198.51.100.81", new { username = "busy_second", password = ApiTestHost.TestPassword });
            await LimitedAsync(rejected);
            TestAssert.Equal(1, blocking.HashCalls, "Reject concurrency overflow before expensive password work.");
        }
        finally { blocking.Release(); }
        using var completed = await first;
        TestAssert.Equal(HttpStatusCode.Created, completed.StatusCode, "The admitted operation finishes normally.");
        using var next = await SendAsync(limited.Client, HttpMethod.Post, "/registration", "198.51.100.81", new { username = "busy_after", password = ApiTestHost.TestPassword });
        TestAssert.Equal(HttpStatusCode.Created, next.StatusCode, "Completion returns its concurrency permit.");
    }

    public static async Task SecurityLogsAreSampledAndNeverContainCredentialsOrTokens(ApiTestHost host)
    {
        var logs = new SecurityLogCollector();
        await using var limited = await host.CreateSecurityHostAsync(Relax, logs);
        var user = await limited.RegisterAsync("audit_user");
        const string secret = "do_not_log_this_passphrase";
        for (var i = 0; i < 3; i++)
        {
            using var failed = await SendAsync(limited.Client, HttpMethod.Post, "/auth/login", "198.51.100.90", new { username = user.Username, password = secret });
            await ApiAssertions.ErrorAsync(failed, HttpStatusCode.Unauthorized, "invalid_credentials");
        }
        TestAssert.Equal(1, logs.Messages.Count(message => message.Contains("authentication_failed")), "Sample repeated failures in one category/bucket/window.");
        var login = await limited.LoginAsync(user);
        using var badToken = await SendAsync(limited.Client, HttpMethod.Get, "/api/users/me?access_token=private-query-marker", "198.51.100.91", token: "private-invalid-bearer-marker");
        await ApiAssertions.ErrorAsync(badToken, HttpStatusCode.Unauthorized, "unauthorized");
        const string replacement = "do_not_log_replacement_987";
        using var changed = await SendAsync(limited.Client, HttpMethod.Post, "/api/users/me/password", "198.51.100.92", new { currentPassword = ApiTestHost.TestPassword, newPassword = replacement }, login.AccessToken);
        TestAssert.Equal(HttpStatusCode.NoContent, changed.StatusCode, "The sensitive action should succeed.");
        var entries = string.Join('\n', logs.Messages);
        TestAssert.True(entries.Contains("password_changed") && entries.Contains("request"), "Keep useful operation/outcome/correlation metadata.");
        foreach (var forbidden in new[] { secret, replacement, ApiTestHost.TestPassword, user.Username, login.AccessToken, "private-query-marker", "private-invalid-bearer-marker", "198.51.100.90" })
            TestAssert.True(!entries.Contains(forbidden, StringComparison.Ordinal), "Security logs must omit secret and raw request values.");
    }

    public static async Task InvalidSecuritySettingsFailStartup(ApiTestHost host)
    {
        foreach (var configure in new Action<SecurityLimitsOptions>[] {
            options => options.Window = TimeSpan.Zero,
            options => options.LoginPerAccount = 0,
            options => { options.MaximumConnections = 1; options.MaximumConnectionsPerUser = 2; }
        })
        {
            var failed = false;
            try { await using var invalid = await host.CreateSecurityHostAsync(configure); }
            catch (OptionsValidationException) { failed = true; }
            TestAssert.True(failed, "Reject invalid deployment security settings before serving requests.");
        }
    }

    private static void Relax(SecurityLimitsOptions options)
    {
        options.RequestsPerServer = 100000; options.RequestsPerIp = 10000;
        options.RegistrationPerIp = 100; options.LoginPerIp = 100; options.LoginPerAccount = 100;
        options.SessionsPerIp = 1000; options.PasswordChangesPerUser = 100;
        options.HubCallsPerUser = 1000; options.MessagesPerUser = 1000; options.ConnectionAttemptsPerUser = 1000;
        options.MaximumConnectionsPerUser = 10; options.MaximumConnections = 100;
    }
    private static Task<HttpResponseMessage> LoginAttemptAsync(ApiTestHost host, string username, string peer) =>
        SendAsync(host.Client, HttpMethod.Post, "/auth/login", peer, new { username, password = "incorrect passphrase" });
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string peer, object? body = null, string? token = null, string? forwarded = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Test-Peer-IP", peer);
        if (forwarded is not null) request.Headers.Add("X-Forwarded-For", forwarded);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
    private static async Task LimitedAsync(HttpResponseMessage response)
    {
        await ApiAssertions.ErrorAsync(response, HttpStatusCode.TooManyRequests, "rate_limited");
        TestAssert.True(response.Headers.RetryAfter?.Delta >= TimeSpan.FromSeconds(1), "Return retry guidance with the standard error envelope.");
    }
    private static async Task RateErrorAsync(Func<Task> action)
    {
        try { await action(); }
        catch (HubException exception) when (exception.Message.Contains("rate_limited:", StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("Expected a safe message-rate rejection.");
    }
    private static async Task RejectConnectionAsync(ApiTestHost host, string token)
    {
        try
        {
            await using var rejected = await SignalRTestClient.ConnectAsync(host, token);
            await rejected.Disconnected.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception exception) when (exception is HttpRequestException or HubException or IOException or InvalidOperationException) { }
    }
}
