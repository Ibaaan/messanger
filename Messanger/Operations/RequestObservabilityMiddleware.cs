using System.Diagnostics;

namespace Messanger.Operations;

internal sealed class RequestObservabilityMiddleware(RequestDelegate next, OperationalMetrics metrics, ILogger<RequestObservabilityMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Request-ID"] = context.TraceIdentifier;
        var operation = context.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName ?? "unmatched";
        var started = Stopwatch.GetTimestamp();
        var status = 500;
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["RequestId"] = context.TraceIdentifier });
        try { await next(context); status = context.Response.StatusCode; }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            metrics.Request(operation, context.Request.Method, status, elapsed);
            logger.LogInformation("HTTP operation {Operation} returned {StatusCode} in {ElapsedMilliseconds}ms; request {RequestId}", operation, status, elapsed, context.TraceIdentifier);
        }
    }
}
