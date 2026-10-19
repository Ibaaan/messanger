using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Messanger.Application;
using Messanger.Application.Accounts;
using Messanger.Application.Authentication;
using Messanger.Application.Persistence;
using Messanger.Infrastructure.Persistence;
using Messanger.IntegrationTests.Support;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Messanger.IntegrationTests.Api;

internal static class PasswordChangeTests
{
    private const string NewPassword = "replacement passphrase 456";

    public static async Task ChangeRevokesEverySessionAndClosesLiveConnections(ApiTestHost host)
    {
        var group = await ChatTestGroup.CreateAsync(host);
        using var browser = await SessionBrowser.CreateAsync(host);
        var login = await LoginAsync(browser, group.Owner.Username, ApiTestHost.TestPassword);
        await using var joined = await SignalRTestClient.ConnectAsync(host, login.AccessToken);
        await using var idle = await SignalRTestClient.ConnectAsync(host, group.OwnerToken);
        await joined.Connection.InvokeAsync("JoinChat", group.Chat.Id);
        var closed = Task.WhenAll(ClosedAsync(joined), ClosedAsync(idle));
        using var changed = await ChangeAsync(browser.Client, login.AccessToken, ApiTestHost.TestPassword, NewPassword, group.Member.Id);
        TestAssert.Equal(HttpStatusCode.NoContent, changed.StatusCode, "A user can change their own password.");
        TestAssert.Equal("", await changed.Content.ReadAsStringAsync(), "Do not return credentials or hashes.");
        await closed.WaitAsync(TimeSpan.FromSeconds(10));
        TestAssert.True(browser.Cookies.GetCookies(new Uri("https://localhost/auth/refresh"))["__Secure-MessangerRefresh"] is null,
            "Clear the caller's persistent cookie using its auth path.");
        foreach (var token in new[] { login.AccessToken, group.OwnerToken })
        {
            using var denied = await host.GetCurrentUserAsync(token);
            await ApiAssertions.ErrorAsync(denied, HttpStatusCode.Forbidden, "forbidden");
        }
        using var unaffected = await host.GetCurrentUserAsync(group.MemberToken);
        unaffected.EnsureSuccessStatusCode();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
            var user = await db.Users.SingleAsync(user => user.Id == group.Owner.Id);
            TestAssert.Equal(1, user.SessionVersion, "Invalidate access tokens across every family.");
            TestAssert.True(user.PasswordHash != NewPassword && scope.ServiceProvider.GetRequiredService<IPasswordService>().Verify(user, NewPassword), "Store a verified salted hash.");
            TestAssert.True(!await db.RefreshTokens.AnyAsync(token => token.UserId == user.Id && token.RevokedAt == null), "Revoke all existing refresh families atomically.");
            TestAssert.Equal(0, (await db.Users.SingleAsync(user => user.Id == group.Member.Id)).SessionVersion,
                "Ignore a client-supplied userId; the target comes exclusively from the Bearer subject.");
        }
        using var oldLogin = await host.Client.PostAsJsonAsync("/auth/login", new { username = group.Owner.Username, password = ApiTestHost.TestPassword });
        await ApiAssertions.ErrorAsync(oldLogin, HttpStatusCode.Unauthorized, "invalid_credentials");
        var fresh = await LoginAsync(browser, group.Owner.Username, NewPassword);
        using var restored = await host.GetCurrentUserAsync(fresh.AccessToken);
        restored.EnsureSuccessStatusCode();
    }

    public static async Task WrongCurrentPasswordPreservesTheExistingSession(ApiTestHost host)
    {
        var user = await host.RegisterAsync();
        using var browser = await SessionBrowser.CreateAsync(host);
        var login = await LoginAsync(browser, user.Username, ApiTestHost.TestPassword);
        var cookie = browser.Cookies.GetCookieHeader(new Uri("https://localhost/auth/refresh"));
        using var wrong = await ChangeAsync(browser.Client, login.AccessToken, "incorrect passphrase", NewPassword);
        await ApiAssertions.ErrorAsync(wrong, HttpStatusCode.Unauthorized, "invalid_credentials");
        TestAssert.Equal(cookie, browser.Cookies.GetCookieHeader(new Uri("https://localhost/auth/refresh")), "A failed confirmation must not clear cookies.");
        using var current = await host.GetCurrentUserAsync(login.AccessToken);
        current.EnsureSuccessStatusCode();
        using var refreshed = await browser.Client.PostAsync("/auth/refresh", null);
        refreshed.EnsureSuccessStatusCode();
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
        TestAssert.Equal(0, (await db.Users.SingleAsync(item => item.Id == user.Id)).SessionVersion, "Failed verification leaves the version unchanged.");
    }

    public static async Task PasswordContractsRequireAuthenticationHttpsAndValidInput(ApiTestHost host)
    {
        var user = await host.RegisterAsync();
        var token = (await host.LoginAsync(user)).AccessToken;
        foreach (var password in new string?[] { null, "", "short", new('x', 257), new(' ', 12) })
        {
            using var invalid = await ChangeAsync(host.Client, token, ApiTestHost.TestPassword, password);
            await ApiAssertions.ErrorAsync(invalid, HttpStatusCode.UnprocessableEntity, "validation_failed");
        }
        using var missingCurrent = await ChangeAsync(host.Client, token, null, NewPassword);
        await ApiAssertions.ErrorAsync(missingCurrent, HttpStatusCode.UnprocessableEntity, "validation_failed");
        using var anonymous = await host.Client.PostAsJsonAsync("/api/users/me/password", new { currentPassword = ApiTestHost.TestPassword, newPassword = NewPassword });
        await ApiAssertions.ErrorAsync(anonymous, HttpStatusCode.Unauthorized, "unauthorized");
        using var browser = await SessionBrowser.CreateAsync(host);
        await LoginAsync(browser, user.Username, ApiTestHost.TestPassword);
        using var cookieOnly = await browser.Client.PostAsJsonAsync("/api/users/me/password", new { currentPassword = ApiTestHost.TestPassword, newPassword = NewPassword });
        await ApiAssertions.ErrorAsync(cookieOnly, HttpStatusCode.Unauthorized, "unauthorized");
        using var http = await ChangeAsync(host.Client, token, ApiTestHost.TestPassword, NewPassword, path: "http://localhost/api/users/me/password");
        await ApiAssertions.ErrorAsync(http, HttpStatusCode.BadRequest, "https_required");
        using var active = await host.GetCurrentUserAsync(token);
        active.EnsureSuccessStatusCode();
    }

    public static async Task ConcurrentVerifiedChangesHaveExactlyOneWinner(ApiTestHost host)
    {
        var response = await host.RegisterAsync();
        var token = (await host.LoginAsync(response)).AccessToken;
        await using var first = host.Services.CreateAsyncScope();
        await using var second = host.Services.CreateAsyncScope();
        var db = first.ServiceProvider.GetRequiredService<MessangerDbContext>();
        var user = await db.Users.SingleAsync(item => item.Id == response.Id);
        var hashes = first.ServiceProvider.GetRequiredService<IPasswordService>();
        var firstHash = hashes.Hash(user, NewPassword);
        var secondHash = hashes.Hash(user, "another replacement password");
        async Task<bool> AttemptAsync(IServiceProvider services, string replacement)
        {
            try { await services.GetRequiredService<IAccountRepository>().ChangePasswordAsync(user.Id, 0, user.PasswordHash, replacement, default); return true; }
            catch (UseCaseException exception) when (exception.Code == "account_conflict") { return false; }
        }
        var winners = await Task.WhenAll(AttemptAsync(first.ServiceProvider, firstHash), AttemptAsync(second.ServiceProvider, secondHash));
        TestAssert.Equal(1, winners.Count(winner => winner), "One verified hash/version can be consumed only once.");
        await using var verification = host.Services.CreateAsyncScope();
        var stored = await verification.ServiceProvider.GetRequiredService<MessangerDbContext>().Users.SingleAsync(item => item.Id == user.Id);
        TestAssert.Equal(1, stored.SessionVersion, "Only the winning change advances the version.");
        TestAssert.Equal(winners[0] ? firstHash : secondHash, stored.PasswordHash, "The losing request cannot overwrite the winner.");
        using var revoked = await host.GetCurrentUserAsync(token);
        await ApiAssertions.ErrorAsync(revoked, HttpStatusCode.Forbidden, "forbidden");
    }

    public static async Task LoginAndRefreshRacesCannotPreserveAnOldPasswordSession(ApiTestHost host)
    {
        foreach (var refresh in new[] { true, false })
        {
            var user = await host.RegisterAsync();
            using var browser = await SessionBrowser.CreateAsync(host);
            var login = await LoginAsync(browser, user.Username, ApiTestHost.TestPassword);
            var competing = refresh ? browser.Client.PostAsync("/auth/refresh", null)
                : host.Client.PostAsJsonAsync("/auth/login", new { username = user.Username, password = ApiTestHost.TestPassword });
            var changing = ChangeAsync(host.Client, login.AccessToken, ApiTestHost.TestPassword, NewPassword);
            var responses = await Task.WhenAll(competing, changing);
            using var other = responses[0];
            using var changed = responses[1];
            TestAssert.Equal(HttpStatusCode.NoContent, changed.StatusCode, "The password change must commit.");
            TestAssert.True(other.StatusCode is HttpStatusCode.OK or HttpStatusCode.Unauthorized, "The competing request either finishes before revocation or is rejected.");
            if (other.IsSuccessStatusCode)
            {
                var old = (await other.Content.ReadFromJsonAsync<LoginResponse>())!;
                using var denied = await host.GetCurrentUserAsync(old.AccessToken);
                await ApiAssertions.ErrorAsync(denied, HttpStatusCode.Forbidden, "forbidden");
            }
            await using var scope = host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
            TestAssert.True(!await db.RefreshTokens.AnyAsync(token => token.UserId == user.Id && token.RevokedAt == null), "No login/refresh with the old password version may survive.");
        }
    }

    public static async Task InactiveAccountsCannotChangePasswordsFromPreviouslyTrackedScopes(ApiTestHost host)
    {
        foreach (var status in new[] { "SUSPENDED", "DELETED" })
        {
            var user = await host.RegisterAsync();
            var token = (await host.LoginAsync(user)).AccessToken;
            await using var scope = host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
            await db.Users.SingleAsync(item => item.Id == user.Id);
            await host.SetStatusAsync(user.Id, status);
            var rejected = false;
            try { await scope.ServiceProvider.GetRequiredService<PasswordChangeService>().ChangeAsync(user.Id, 0, ApiTestHost.TestPassword, NewPassword, default); }
            catch (UseCaseException exception) when (exception.Code == "forbidden") { rejected = true; }
            TestAssert.True(rejected, "Recheck current status rather than the earlier tracked user.");
            using var api = await ChangeAsync(host.Client, token, ApiTestHost.TestPassword, NewPassword);
            await ApiAssertions.ErrorAsync(api, HttpStatusCode.Forbidden, "forbidden");
        }
    }

    private static async Task<HttpResponseMessage> ChangeAsync(HttpClient client, string token, string? current, string? replacement,
        long? userId = null, string path = "/api/users/me/password")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(new { currentPassword = current, newPassword = replacement, userId }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static async Task<LoginResponse> LoginAsync(SessionBrowser browser, string username, string password)
    {
        using var response = await browser.Client.PostAsJsonAsync("/auth/login", new { username, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    private static Task ClosedAsync(SignalRTestClient client)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Connection.Closed += _ => { completion.TrySetResult(); return Task.CompletedTask; };
        return completion.Task;
    }
}
