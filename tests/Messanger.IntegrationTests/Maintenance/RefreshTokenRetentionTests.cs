using System.Net;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using Messanger.Application.Authentication;
using Messanger.Application.Persistence;
using Messanger.Domain;
using Messanger.Infrastructure.Persistence;
using Messanger.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Messanger.IntegrationTests.Maintenance;

internal static class RefreshTokenRetentionTests
{
    public static async Task OnlyRecordsExpiredForAtLeastNinetyDaysAreEligible(ApiTestHost host)
    {
        var clock = Clock();
        await using var isolated = await host.CreateClockHostAsync(clock);
        var user = await isolated.RegisterAsync();
        var cutoff = clock.GetUtcNow().AddDays(-90);
        var rows = new[] {
            Token(user.Id, cutoff.AddSeconds(-1)), Token(user.Id, cutoff, revokedAt: clock.GetUtcNow()),
            Token(user.Id, cutoff.AddTicks(10)), Token(user.Id, clock.GetUtcNow().AddDays(-1)),
            Token(user.Id, clock.GetUtcNow().AddDays(1), revokedAt: clock.GetUtcNow()), Token(user.Id, clock.GetUtcNow().AddDays(2))
        };
        await SeedAsync(isolated, rows);
        var result = await RunAsync(isolated, 2, 10);
        TestAssert.Equal(cutoff, result.Cutoff, "Capture the exact 90-day expiry cutoff.");
        TestAssert.Equal(2, result.DeletedRows, "Purge old unrevoked records and records exactly at the inclusive boundary.");
        var remaining = await RowsAsync(isolated);
        TestAssert.True(rows.Skip(2).All(row => remaining.Any(stored => stored.Id == row.Id)), "Preserve recent expiries and unexpired records, even if revoked.");
    }

    public static async Task NewerFamilyRecordsPreserveOlderReplayEvidence(ApiTestHost host)
    {
        var clock = Clock();
        await using var isolated = await host.CreateClockHostAsync(clock);
        var user = await isolated.RegisterAsync();
        var cutoff = clock.GetUtcNow().AddDays(-90);
        var recentFamily = Guid.NewGuid();
        var activeFamily = Guid.NewGuid();
        await SeedAsync(isolated, Token(user.Id, cutoff.AddDays(-1), recentFamily), Token(user.Id, cutoff.AddTicks(10), recentFamily),
            Token(user.Id, cutoff.AddDays(-10), activeFamily), Token(user.Id, clock.GetUtcNow().AddDays(1), activeFamily));
        TestAssert.Equal(0, (await RunAsync(isolated)).DeletedRows, "A family is retained while even one record remains inside retention.");
        clock.Advance(TimeSpan.FromTicks(10));
        TestAssert.Equal(2, (await RunAsync(isolated)).DeletedRows, "Once its final record ages past the boundary, the expired family becomes eligible.");
        TestAssert.True((await RowsAsync(isolated)).All(row => row.FamilyId == activeFamily), "Active-family evidence must remain intact.");
    }

    public static async Task BatchesAreBoundedResumableAndIdempotent(ApiTestHost host)
    {
        var clock = Clock();
        await using var isolated = await host.CreateClockHostAsync(clock);
        var user = await isolated.RegisterAsync();
        await SeedAsync(isolated, Enumerable.Range(0, 7).Select(_ => Token(user.Id, clock.GetUtcNow().AddDays(-100))).ToArray());
        var first = await RunAsync(isolated, 2, 2);
        TestAssert.Equal(4, first.DeletedRows, "Respect both per-batch and per-pass limits.");
        TestAssert.True(first.BatchLimitReached, "Report the full final batch without claiming an exact remaining backlog.");
        TestAssert.Equal(3, (await RowsAsync(isolated)).Length, "Leave the remaining work for another pass.");
        var next = await RunAsync(isolated, 2, 2);
        TestAssert.Equal(3, next.DeletedRows, "Resume safely after an incomplete pass.");
        TestAssert.True(!next.BatchLimitReached, "Stop when a batch is not full.");
        TestAssert.Equal(0, (await RunAsync(isolated)).DeletedRows, "Repeated passes on an empty backlog are harmless.");
    }

    public static async Task ConcurrentCleanersNeverDeleteTheSameRowTwice(ApiTestHost host)
    {
        var clock = Clock();
        await using var isolated = await host.CreateClockHostAsync(clock);
        var user = await isolated.RegisterAsync();
        await SeedAsync(isolated, Enumerable.Range(0, 20).Select(_ => Token(user.Id, clock.GetUtcNow().AddDays(-100))).ToArray());
        await using var first = isolated.Services.CreateAsyncScope();
        await using var second = isolated.Services.CreateAsyncScope();
        var cutoff = clock.GetUtcNow().AddDays(-90);
        var counts = await Task.WhenAll(
            first.ServiceProvider.GetRequiredService<IRefreshTokenCleanupRepository>().DeleteBatchAsync(cutoff, 7, default),
            second.ServiceProvider.GetRequiredService<IRefreshTokenCleanupRepository>().DeleteBatchAsync(cutoff, 7, default));
        TestAssert.Equal(14, counts.Sum(), "Independent workers claim disjoint bounded batches.");
        TestAssert.Equal(6, (await RowsAsync(isolated)).Length, "No row should be counted twice.");
        TestAssert.Equal(6, (await RunAsync(isolated)).DeletedRows, "A final pass drains the remaining backlog.");
    }

    public static async Task LockedRecordsAreSkippedRatherThanBlockingCleanup(ApiTestHost host)
    {
        var clock = Clock();
        await using var isolated = await host.CreateClockHostAsync(clock);
        var user = await isolated.RegisterAsync();
        var locked = Token(user.Id, clock.GetUtcNow().AddDays(-101));
        await SeedAsync(isolated, locked, Token(user.Id, clock.GetUtcNow().AddDays(-100)), Token(user.Id, clock.GetUtcNow().AddDays(-100)));
        await using (var scope = isolated.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.RefreshTokens.FromSqlInterpolated($"SELECT * FROM refresh_token_families WHERE id = {locked.Id} FOR UPDATE").AsNoTracking().SingleAsync();
            TestAssert.Equal(2, (await RunAsync(isolated).WaitAsync(TimeSpan.FromSeconds(10))).DeletedRows, "Skip a competing row lock and continue processing other records.");
            await transaction.RollbackAsync();
        }
        TestAssert.Equal(1, (await RunAsync(isolated)).DeletedRows, "Retry the skipped record in a later pass.");
    }

    public static async Task ActiveSessionsAndConsumedTokenReplayDetectionSurviveCleanup(ApiTestHost host)
    {
        var clock = Clock();
        await using var isolated = await host.CreateClockHostAsync(clock);
        var user = await isolated.RegisterAsync();
        using var browser = await SessionBrowser.CreateAsync(isolated);
        using var login = await browser.Client.PostAsJsonAsync("/auth/login", new { username = user.Username, password = ApiTestHost.TestPassword });
        login.EnsureSuccessStatusCode();
        var original = browser.Cookies.GetCookies(new Uri("https://localhost/auth/refresh"))["__Secure-MessangerRefresh"]!.Value;
        var family = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(original).Claims.Single(claim => claim.Type == "sid").Value);
        using var rotated = await browser.Client.PostAsync("/auth/refresh", null);
        rotated.EnsureSuccessStatusCode();
        var session = (await rotated.Content.ReadFromJsonAsync<LoginResponse>())!;
        await SeedAsync(isolated, Token(user.Id, clock.GetUtcNow().AddDays(-100)), Token(user.Id, clock.GetUtcNow().AddDays(-100), family));
        TestAssert.Equal(1, (await RunAsync(isolated)).DeletedRows, "Only the unrelated old family is eligible.");
        using var active = await isolated.GetCurrentUserAsync(session.AccessToken);
        active.EnsureSuccessStatusCode();
        var replayCookies = new CookieContainer();
        replayCookies.Add(new Cookie("__Secure-MessangerRefresh", original, "/auth", "localhost") { Secure = true, HttpOnly = true });
        using var replay = await SessionBrowser.CreateAsync(isolated, replayCookies);
        using var reused = await replay.Client.PostAsync("/auth/refresh", null);
        await ApiAssertions.ErrorAsync(reused, HttpStatusCode.Unauthorized, "session_expired");
        using var revoked = await isolated.GetCurrentUserAsync(session.AccessToken);
        await ApiAssertions.ErrorAsync(revoked, HttpStatusCode.Forbidden, "forbidden");
    }

    public static async Task CancellationKeepsCommittedBatchesAndAllowsALaterResume(ApiTestHost host)
    {
        var clock = Clock();
        await using var isolated = await host.CreateClockHostAsync(clock);
        var user = await isolated.RegisterAsync();
        await SeedAsync(isolated, Enumerable.Range(0, 5).Select(_ => Token(user.Id, clock.GetUtcNow().AddDays(-100))).ToArray());
        await using var scope = isolated.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRefreshTokenCleanupRepository>();
        using var cancellation = new CancellationTokenSource();
        using var cancelAfterBatch = new CleanupRepositoryStub(async (cutoff, size, ct) =>
        {
            var count = await repository.DeleteBatchAsync(cutoff, size, ct);
            cancellation.Cancel();
            return count;
        }, () => { });
        var cancelled = false;
        try { await new RefreshTokenCleanupService(cancelAfterBatch, clock).RunAsync(2, 10, cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        TestAssert.True(cancelled, "Cancellation must stop before the next batch.");
        TestAssert.Equal(3, (await RowsAsync(isolated)).Length, "Already committed cleanup is retained after cancellation.");
        TestAssert.Equal(3, (await RunAsync(isolated)).DeletedRows, "A fresh pass safely resumes remaining work.");
    }

    private static MutableClock Clock() => new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    private static RefreshTokenFamily Token(long userId, DateTimeOffset expiresAt, Guid? family = null, DateTimeOffset? revokedAt = null) => new()
    {
        UserId = userId, FamilyId = family ?? Guid.NewGuid(), JtiHash = Guid.NewGuid().ToString("N").PadRight(64, '0'),
        IssuedAt = expiresAt.AddDays(-30), ExpiresAt = expiresAt, RevokedAt = revokedAt
    };
    private static async Task SeedAsync(ApiTestHost host, params RefreshTokenFamily[] tokens)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
        db.RefreshTokens.AddRange(tokens);
        await db.SaveChangesAsync();
    }
    private static async Task<RefreshTokenFamily[]> RowsAsync(ApiTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MessangerDbContext>().RefreshTokens.AsNoTracking().ToArrayAsync();
    }
    private static async Task<RefreshTokenCleanupResult> RunAsync(ApiTestHost host, int batchSize = 1000, int maximumBatches = 10)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<RefreshTokenCleanupService>().RunAsync(batchSize, maximumBatches, default);
    }
}
