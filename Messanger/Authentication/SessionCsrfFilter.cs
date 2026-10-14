using Microsoft.AspNetCore.Antiforgery;
using Messanger.Http;

namespace Messanger.Authentication;

internal sealed class SessionCsrfFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (!http.Request.IsHttps)
        {
            await ApiError.WriteAsync(http, 400, "https_required", "Use HTTPS for persistent sign-in.");
            return Results.Empty;
        }
        var origin = http.Request.Headers.Origin;
        if ((origin.Count > 0 && origin.ToString() != $"https://{http.Request.Host}")
            || http.Request.Headers["Sec-Fetch-Site"] == "cross-site")
        {
            await ApiError.WriteAsync(http, 403, "csrf_failed", "Use a same-origin session request.");
            return Results.Empty;
        }
        if (!HttpMethods.IsGet(http.Request.Method))
            try { await antiforgery.ValidateRequestAsync(http); }
            catch (AntiforgeryValidationException)
            {
                await ApiError.WriteAsync(http, 403, "csrf_failed", "A valid CSRF token is required.");
                return Results.Empty;
            }
        return await next(context);
    }
}
