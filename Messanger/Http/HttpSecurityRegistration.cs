using System.Net;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Messanger.Http;

internal static class HttpSecurityRegistration
{
    public static IServiceCollection AddHttpSecurity(this IServiceCollection services, IConfiguration configuration,
        bool development)
    {
        services.AddOptions<HttpSecurityOptions>().Bind(configuration.GetSection("HttpSecurity"))
            .Validate(options => options.AllowedOrigins.All(origin =>
                    Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                    && (uri.Scheme == "https" || (development && uri.Scheme == "http"))
                    && uri.GetLeftPart(UriPartial.Authority) == origin
                    && string.IsNullOrEmpty(uri.UserInfo)),
                "Allowed origins must be exact origins; production requires HTTPS.")
            .Validate(options => options.TrustedProxies.All(proxy => IPAddress.TryParse(proxy, out _)),
                "Trusted proxies must be IP addresses.")
            .ValidateOnStart();
        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<HttpSecurityOptions>>((options, security) =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            foreach (var proxy in security.Value.TrustedProxies)
                options.KnownProxies.Add(IPAddress.Parse(proxy));
        });
        services.AddCors();
        services.AddOptions<CorsOptions>()
            .Configure<IOptions<HttpSecurityOptions>>((options, security) =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    if (security.Value.AllowedOrigins.Length > 0)
                        policy.WithOrigins(security.Value.AllowedOrigins);
                    policy.WithMethods("GET", "POST", "DELETE").WithHeaders("Authorization", "Content-Type", "X-Requested-With", "X-SignalR-User-Agent");
                });
            });
        return services;
    }

    public static IApplicationBuilder UseCoreHttpSecurity(this IApplicationBuilder app, bool development)
    {
        app.UseForwardedHeaders();
        if (!development) app.UseHsts();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            var socketOrigin = $"{(context.Request.IsHttps ? "wss" : "ws")}://{context.Request.Host}";
            context.Response.Headers.ContentSecurityPolicy = $"default-src 'self'; script-src 'self'; connect-src 'self' {socketOrigin}; style-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
            if (!development && !context.Request.IsHttps)
            {
                await ApiError.WriteAsync(context, 400, "https_required", "HTTPS is required.");
                return;
            }

            if (context.Request.ContentLength > 16 * 1024)
            {
                await ApiError.WriteAsync(context, 413, "payload_too_large", "The request body is too large.");
                return;
            }

            var origin = context.Request.Headers.Origin;
            var security = context.RequestServices.GetRequiredService<IOptions<HttpSecurityOptions>>().Value;
            if (origin.Count > 0 &&
                (origin.Count != 1 || !security.AllowedOrigins.Contains(origin[0], StringComparer.Ordinal)))
            {
                await ApiError.WriteAsync(context, 403, "origin_not_allowed", "This browser origin is not allowed.");
                return;
            }

            await next(context);
        });
        app.UseCors();
        return app;
    }
}