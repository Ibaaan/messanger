using System.Net;
using System.Net.Http.Json;
using Messanger.Infrastructure.Persistence;
using Messanger.IntegrationTests.Support;
using Messanger.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Messanger.IntegrationTests.Api;

internal static class OperationsTests
{
    public static async Task ReadinessDetectsPendingMigrationsAndRecovers(ApiTestHost host)
    {
        await using var isolated = await host.CreateSecurityHostAsync(options => options.Enabled = false, overrides: services =>
            services.PostConfigure<OperationalOptions>(options => options.ReadinessCache = TimeSpan.Zero));
        using var healthy = await isolated.Client.GetAsync("/health/ready");
        healthy.EnsureSuccessStatusCode();
        await using var scope = isolated.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
        var latest = (await db.Database.GetAppliedMigrationsAsync()).Last();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = {latest}");
        using var pending = await isolated.Client.GetAsync("/health/ready");
        TestAssert.Equal(HttpStatusCode.ServiceUnavailable, pending.StatusCode, "Missing migrations must remove the instance from ready traffic.");
        using var live = await isolated.Client.GetAsync("/health/live");
        live.EnsureSuccessStatusCode();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({latest}, {"10.0.14"})");
        using var restored = await isolated.Client.GetAsync("/health/ready");
        restored.EnsureSuccessStatusCode();
    }

    public static async Task DatabaseOutageDoesNotBreakLivenessOrExposeConnectionDetails(ApiTestHost host)
    {
        await using var broken = await host.CreateSecurityHostAsync(options => options.Enabled = false, overrides: services =>
        {
            services.RemoveAll<NpgsqlDataSource>();
            services.AddSingleton(_ => new NpgsqlDataSourceBuilder("Host=127.0.0.1;Port=1;Database=missing;Username=private_user;Password=private_password;Timeout=1") { Name = "messanger" }.Build());
            services.PostConfigure<OperationalOptions>(options => options.ReadinessCache = TimeSpan.Zero);
        });
        using var live = await broken.Client.GetAsync("/health/live");
        live.EnsureSuccessStatusCode();
        using var ready = await broken.Client.GetAsync("/health/ready");
        TestAssert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode, "An unavailable dependency fails readiness.");
        var body = await ready.Content.ReadAsStringAsync();
        TestAssert.True(!body.Contains("private_") && !body.Contains("127.0.0.1") && !body.Contains("Npgsql"), "Public probes must not expose database details.");
    }

    public static async Task CorrelationAndMetricsAreSafeAndRequireACurrentAdministrator(ApiTestHost host)
    {
        var logs = new SecurityLogCollector();
        await using var isolated = await host.CreateSecurityHostAsync(options => options.Enabled = false, logs);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users/me?private_query_marker=do_not_log");
        request.Headers.Add("X-Request-ID", "private_client_correlation");
        using var rejected = await isolated.Client.SendAsync(request);
        var id = rejected.Headers.GetValues("X-Request-ID").Single();
        TestAssert.True(id.Length > 0 && id != "private_client_correlation", "Generate server correlation IDs rather than trusting arbitrary headers.");
        var envelope = await rejected.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        TestAssert.Equal(id, envelope.GetProperty("error").GetProperty("requestId").GetString()!, "Errors and response headers share correlation IDs.");
        var admin = await isolated.RegisterAsync();
        await using (var scope = isolated.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<MessangerDbContext>().Users.Where(user => user.Id == admin.Id).ExecuteUpdateAsync(update => update.SetProperty(user => user.Role, "ADMIN"));
        var token = (await isolated.LoginAsync(admin)).AccessToken;
        using var metrics = await isolated.SendAsAsync(HttpMethod.Get, "/ops/metrics", token);
        metrics.EnsureSuccessStatusCode();
        var snapshot = (await metrics.Content.ReadFromJsonAsync<MetricsSnapshot>())!;
        TestAssert.True(snapshot.Requests.Any(metric => metric.Operation == "GetCurrentUser" && metric.StatusClass == 4 && metric.Count > 0), "Record bounded route/status/latency measurements.");
        TestAssert.True(snapshot.DatabaseCommands > 0 && snapshot.DatabaseCommandMilliseconds >= 0, "Collect Npgsql timings without SQL text.");
        await using (var scope = isolated.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<MessangerDbContext>().Users.Where(user => user.Id == admin.Id).ExecuteUpdateAsync(update => update.SetProperty(user => user.Role, "USER"));
        using var demoted = await isolated.SendAsAsync(HttpMethod.Get, "/ops/metrics", token);
        await ApiAssertions.ErrorAsync(demoted, HttpStatusCode.Forbidden, "forbidden");
        var entries = string.Join('\n', logs.Messages);
        TestAssert.True(!entries.Contains("private_query_marker") && !entries.Contains("private_client_correlation") && !entries.Contains("do_not_log"), "Structured request logs omit raw paths/queries and client correlation data.");
    }

}
