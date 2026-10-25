using Messanger.Application.Persistence;
using Messanger.Chats;
using Messanger.Http;

namespace Messanger.Operations;

internal static class OperationalEndpoints
{
    public static IServiceCollection AddOperations(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<OperationalMetrics>();
        services.AddSingleton<ReadinessMonitor>();
        services.AddOptions<OperationalOptions>().Bind(configuration.GetSection("Operations"))
            .Validate(options => options.ReadinessTimeout >= TimeSpan.FromMilliseconds(100) && options.ReadinessTimeout <= TimeSpan.FromSeconds(30), "Readiness timeout must be 0.1–30 seconds.")
            .Validate(options => options.ReadinessCache >= TimeSpan.Zero && options.ReadinessCache <= TimeSpan.FromMinutes(1), "Readiness cache must be 0–60 seconds.")
            .ValidateOnStart();
        return services;
    }
    public static IEndpointRouteBuilder MapOperations(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/live", () => Results.Ok(new { status = "healthy" }))
            .AllowAnonymous().DisableRateLimiting().WithName("Liveness").WithTags("Operations");
        endpoints.MapGet("/health/ready", async (ReadinessMonitor readiness, CancellationToken ct) =>
        {
            var ready = await readiness.CheckAsync(ct);
            return Results.Json(new { status = ready ? "ready" : "unavailable" }, statusCode: ready ? 200 : 503);
        }).AllowAnonymous().DisableRateLimiting().WithName("Readiness").WithTags("Operations").Produces(200).Produces(503);
        endpoints.MapGet("/ops/metrics", async (HttpContext context, IUserRepository users, OperationalMetrics metrics, CancellationToken ct) =>
            (await users.GetByIdAsync(ChatEndpoints.UserId(context.User), ct))?.Role == "ADMIN" ? Results.Ok(metrics.Snapshot()) : Results.Forbid())
            .RequireAuthorization().WithName("Metrics").WithTags("Operations").Produces<MetricsSnapshot>().WithApiErrors();
        return endpoints;
    }
}
