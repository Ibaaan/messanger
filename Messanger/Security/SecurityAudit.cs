using Microsoft.Extensions.Options;
using Messanger.Operations;

namespace Messanger.Security;

public sealed class SecurityAudit(ILogger<SecurityAudit> logger, IOptions<SecurityLimitsOptions> settings, TimeProvider clock, OperationalMetrics metrics)
{
    // Sample denial events once per category/bucket/window to avoid turning rejected floods into log floods.
    private readonly long[] windows = new long[5 * SecurityBudgets.BucketCount];
    public void AuthenticationFailure(HttpContext context, string reason) =>
        Denied(reason == "refresh_replay" ? 4 : 0, context, reason, reason == "refresh_replay" ? "refresh_replay" : "authentication_failed");
    public void RateLimited(HttpContext context, string reason, long? userId = null) => Denied(1, context, reason, "rate_limited", userId);
    public void ConnectionRejected(HttpContext? context, long userId) => Denied(2, context, "hub_connections", "connection_rejected", userId);
    public void AccessDenied(HttpContext context) => Denied(3, context, "access_denied", "access_denied");
    public void Changed(HttpContext context, string action, long userId) =>
        logger.LogInformation(new EventId(1100, "AccountSecurityChanged"), "Security action {Action} for user {UserId}; request {RequestId}",
            action, userId, context.TraceIdentifier);

    private void Denied(int category, HttpContext? context, string reason, string action, long? userId = null)
    {
        metrics.Security(action);
        var key = userId is { } id ? SecurityBudgets.UserKey(id) : context is null ? "unknown" : SecurityBudgets.IpKey(context);
        var slot = category * SecurityBudgets.BucketCount + SecurityBudgets.Bucket(key);
        var window = clock.GetUtcNow().UtcTicks / settings.Value.Window.Ticks + 1;
        if (Interlocked.Exchange(ref windows[slot], window) == window) return;
        var operation = context?.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName ?? "unknown";
        logger.LogWarning(new EventId(1000 + category, action),
            "Security event {Action}; reason {Reason}; operation {Operation}; user {UserId}; request {RequestId}",
            action, reason, operation, userId, context?.TraceIdentifier ?? "none");
    }
}
