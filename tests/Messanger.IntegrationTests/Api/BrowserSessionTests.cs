using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using Messanger.Application;
using Messanger.Application.Authentication;
using Messanger.Application.Persistence;
using Messanger.Infrastructure.Persistence;
using Messanger.IntegrationTests.Support;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Messanger.IntegrationTests.Api;

internal static class BrowserSessionTests
{
    private const string RefreshName = "__Secure-MessangerRefresh";
    private static readonly Uri AuthUri = new("https://localhost/auth/refresh");

    public static async Task LoginIssuesPrivatePersistentCookieAndHashedRecord(ApiTestHost host)
    {
        var user = await host.RegisterAsync();
        using var browser = await SessionBrowser.CreateAsync(host);
        using var response = await LoginAsync(browser, user);
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        var cookie = browser.Cookies.GetCookies(AuthUri)[RefreshName]!;
        TestAssert.True(cookie.HttpOnly && cookie.Secure, "Refresh cookie must be HttpOnly and Secure.");
        TestAssert.Equal("/auth", cookie.Path, "Keep refresh credentials on auth routes.");
        var header = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(RefreshName + "="));
        TestAssert.True(header.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase) && !header.Contains("domain=", StringComparison.OrdinalIgnoreCase), "Use a strict host-only cookie.");
        TestAssert.True(cookie.Expires.ToUniversalTime() > DateTime.UtcNow.AddDays(29), "Browser reopening must preserve the session.");
        TestAssert.True(!(await response.Content.ReadAsStringAsync()).Contains(cookie.Value, StringComparison.Ordinal), "Never return refresh credentials in JSON.");
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
        var row = await db.RefreshTokens.SingleAsync(token => token.UserId == user.Id);
        var refreshJwt = new JwtSecurityTokenHandler().ReadJwtToken(cookie.Value);
        TestAssert.Equal(64, row.JtiHash.Length, "Persist a SHA256 hash.");
        TestAssert.True(row.JtiHash != refreshJwt.Id, "Never persist the raw token identifier.");
        using var bearer = await host.GetCurrentUserAsync(cookie.Value);
        await ApiAssertions.ErrorAsync(bearer, HttpStatusCode.Unauthorized, "unauthorized");
        using var cookieOnly = await browser.Client.GetAsync("/api/users/me");
        await ApiAssertions.ErrorAsync(cookieOnly, HttpStatusCode.Unauthorized, "unauthorized");
        using var access = await host.GetCurrentUserAsync(login.AccessToken);
        access.EnsureSuccessStatusCode();
    }

    public static async Task ReopeningRestoresSessionAndRotatesWithoutExtendingExpiry(ApiTestHost host)
    {
        var user = await host.RegisterAsync();
        using var first = await SessionBrowser.CreateAsync(host);
        using var login = await LoginAsync(first, user);
        var oldCookie = first.Cookies.GetCookies(AuthUri)[RefreshName]!.Value;
        var deadline = first.Cookies.GetCookies(AuthUri)[RefreshName]!.Expires;
        // A fresh client with only persisted cookies models a new tab or a reopened browser.
        using var reopened = await SessionBrowser.CreateAsync(host, first.Cookies);
        using var response = await reopened.Client.PostAsync("/auth/refresh", null);
        response.EnsureSuccessStatusCode();
        var session = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        TestAssert.Equal(user.Id, session.User.Id, "Restore the original identity without credentials.");
        TestAssert.True(oldCookie != reopened.Cookies.GetCookies(AuthUri)[RefreshName]!.Value, "Every refresh rotates the cookie.");
        TestAssert.Equal(deadline, reopened.Cookies.GetCookies(AuthUri)[RefreshName]!.Expires, "Rotation must preserve the family deadline.");
        await using var scope = host.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<MessangerDbContext>().RefreshTokens.Where(token => token.UserId == user.Id).ToArrayAsync();
        TestAssert.Equal(2, rows.Length, "Keep the consumed record for replay detection.");
        var consumed = rows.Single(token => token.RevokedAt is not null);
        TestAssert.Equal(rows.Single(token => token.RevokedAt is null).JtiHash, consumed.ReplacedByJtiHash!, "Link the single successor.");
    }

    public static async Task ReplayedTokenRevokesFamilyAndClosesLiveChat(ApiTestHost host)
    {
        var user = await host.RegisterAsync();
        using var browser = await SessionBrowser.CreateAsync(host);
        using var login = await LoginAsync(browser, user);
        var old = browser.Cookies.GetCookies(AuthUri)[RefreshName]!.Value;
        var unaffected = await host.LoginAsync(user);
        using var rotated = await browser.Client.PostAsync("/auth/refresh", null);
        rotated.EnsureSuccessStatusCode();
        var access = (await rotated.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        await using var live = await SignalRTestClient.ConnectAsync(host, access);
        var closed = OnClosed(live);
        using var attacker = await WithRefreshAsync(host, old);
        using var reused = await attacker.Client.PostAsync("/auth/refresh", null);
        await ApiAssertions.ErrorAsync(reused, HttpStatusCode.Unauthorized, "session_expired");
        await closed.WaitAsync(TimeSpan.FromSeconds(10));
        using var revoked = await host.GetCurrentUserAsync(access);
        await ApiAssertions.ErrorAsync(revoked, HttpStatusCode.Forbidden, "forbidden");
        using var other = await host.GetCurrentUserAsync(unaffected.AccessToken);
        other.EnsureSuccessStatusCode();
    }

    public static async Task ConcurrentRefreshFailsClosedWithoutTwoSuccessors(ApiTestHost host)
    {
        var user = await host.RegisterAsync();
        using var browser = await SessionBrowser.CreateAsync(host);
        using var login = await LoginAsync(browser, user);
        var old = browser.Cookies.GetCookies(AuthUri)[RefreshName]!.Value;
        using var first = await WithRefreshAsync(host, old);
        using var second = await WithRefreshAsync(host, old);
        var responses = await Task.WhenAll(first.Client.PostAsync("/auth/refresh", null), second.Client.PostAsync("/auth/refresh", null));
        try
        {
            TestAssert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK), "Exactly one rotation can consume the old token.");
            TestAssert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Unauthorized), "A duplicate is treated as replay.");
            await using var scope = host.Services.CreateAsyncScope();
            var rows = await scope.ServiceProvider.GetRequiredService<MessangerDbContext>().RefreshTokens.Where(token => token.UserId == user.Id).ToArrayAsync();
            TestAssert.Equal(2, rows.Length, "Never create two successors.");
            TestAssert.True(rows.All(token => token.RevokedAt is not null), "Replay must revoke even the winner's successor.");
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    public static async Task LogoutIsIdempotentAndRevokesOnlyItsFamily(ApiTestHost host)
    {
        var user = await host.RegisterAsync();
        using var browser = await SessionBrowser.CreateAsync(host);
        using var login = await LoginAsync(browser, user);
        var session = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        var other = await host.LoginAsync(user);
        await using var live = await SignalRTestClient.ConnectAsync(host, session.AccessToken);
        var closed = OnClosed(live);
        using var logout = await browser.Client.PostAsync("/auth/logout", null);
        TestAssert.Equal(HttpStatusCode.NoContent, logout.StatusCode, "Logout should succeed.");
        await closed.WaitAsync(TimeSpan.FromSeconds(10));
        TestAssert.True(browser.Cookies.GetCookies(AuthUri)[RefreshName] is null, "Delete the persistent cookie with matching scope.");
        using var again = await browser.Client.PostAsync("/auth/logout", null);
        TestAssert.Equal(HttpStatusCode.NoContent, again.StatusCode, "Logout is idempotent.");
        using var oldAccess = await host.GetCurrentUserAsync(session.AccessToken);
        await ApiAssertions.ErrorAsync(oldAccess, HttpStatusCode.Forbidden, "forbidden");
        using var otherAccess = await host.GetCurrentUserAsync(other.AccessToken);
        otherAccess.EnsureSuccessStatusCode();
    }

    public static async Task SessionEndpointsRequireCsrfProofAndSameOriginHttps(ApiTestHost host)
    {
        var user = await host.RegisterAsync();
        using var browser = await SessionBrowser.CreateAsync(host);
        foreach (var path in new[] { "/auth/login", "/auth/refresh", "/auth/logout" })
        {
            using var missing = new HttpRequestMessage(HttpMethod.Post, path);
            if (path.EndsWith("login")) missing.Content = JsonContent.Create(new { username = user.Username, password = ApiTestHost.TestPassword });
            // Remove the default header to model a forged request while keeping the browser's cookie.
            browser.Client.DefaultRequestHeaders.Remove("X-CSRF-Token");
            using var denied = await browser.Client.SendAsync(missing);
            await ApiAssertions.ErrorAsync(denied, HttpStatusCode.Forbidden, "csrf_failed");
        }
        using var valid = await SessionBrowser.CreateAsync(host);
        using var crossOrigin = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
        crossOrigin.Headers.Add("Origin", ApiTestHost.AllowedOrigin);
        using var crossDenied = await valid.Client.SendAsync(crossOrigin);
        await ApiAssertions.ErrorAsync(crossDenied, HttpStatusCode.Forbidden, "csrf_failed");
        using var http = await valid.Client.GetAsync("http://localhost/auth/csrf");
        await ApiAssertions.ErrorAsync(http, HttpStatusCode.BadRequest, "https_required");
    }

    public static async Task AbsoluteDeadlineCapsAccessAndNeverSlides(ApiTestHost host)
    {
        var clock = new MutableClock();
        await using var isolated = await host.CreateClockHostAsync(clock);
        var user = await isolated.RegisterAsync();
        using var browser = await SessionBrowser.CreateAsync(isolated);
        using var login = await LoginAsync(browser, user);
        var original = new JwtSecurityTokenHandler().ReadJwtToken(browser.Cookies.GetCookies(AuthUri)[RefreshName]!.Value);
        clock.Advance(TimeSpan.FromDays(30) - TimeSpan.FromSeconds(30));
        using var nearDeadline = await browser.Client.PostAsync("/auth/refresh", null);
        nearDeadline.EnsureSuccessStatusCode();
        var session = (await nearDeadline.Content.ReadFromJsonAsync<LoginResponse>())!;
        var access = new JwtSecurityTokenHandler().ReadJwtToken(session.AccessToken);
        TestAssert.Equal(original.ValidTo, access.ValidTo, "Near expiry, cap access at the original family deadline.");
        clock.Advance(TimeSpan.FromSeconds(31));
        using var expired = await browser.Client.PostAsync("/auth/refresh", null);
        await ApiAssertions.ErrorAsync(expired, HttpStatusCode.Unauthorized, "session_expired");
        await using var scope = isolated.Services.CreateAsyncScope();
        var family = Guid.Parse(original.Claims.Single(claim => claim.Type == "sid").Value);
        TestAssert.True(!await scope.ServiceProvider.GetRequiredService<IRefreshSessionRepository>().IsActiveAsync(user.Id, family, default), "Absolute expiry rejects the entire session.");
    }

    private static async Task<HttpResponseMessage> LoginAsync(SessionBrowser browser, UserResponse user)
    {
        var response = await browser.Client.PostAsJsonAsync("/auth/login", new { username = user.Username, password = ApiTestHost.TestPassword });
        response.EnsureSuccessStatusCode();
        return response;
    }

    private static async Task<SessionBrowser> WithRefreshAsync(ApiTestHost host, string token)
    {
        var cookies = new CookieContainer();
        cookies.Add(new Cookie(RefreshName, token, "/auth", "localhost") { Secure = true, HttpOnly = true });
        return await SessionBrowser.CreateAsync(host, cookies);
    }

    private static Task OnClosed(SignalRTestClient client)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Connection.Closed += _ => { completion.TrySetResult(); return Task.CompletedTask; };
        return completion.Task;
    }
}
