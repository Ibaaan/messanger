using Messanger.Application.Authentication;
using Messanger.Http;
using Messanger.Realtime;
using Microsoft.AspNetCore.Antiforgery;

namespace Messanger.Authentication;

internal static class SessionEndpoints
{
    public static IServiceCollection AddBrowserSessions(this IServiceCollection services)
    {
        services.AddScoped<SessionService>();
        services.AddScoped<AccountSession>();
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-Token";
            options.Cookie.Name = "__Host-MessangerCsrf";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
        return services;
    }

    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/auth").AllowAnonymous().WithTags("Authentication")
            .WithDescription("Same-origin HTTPS only. Bootstrap via GET /auth/csrf; every POST requires its X-CSRF-Token header and CSRF cookie. Refresh/logout also use the HttpOnly refresh cookie.")
            .AddEndpointFilter<SessionCsrfFilter>();
        group.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Ok(new CsrfResponse(antiforgery.GetAndStoreTokens(context).RequestToken!)))
            .WithName("GetSessionCsrf").Produces<CsrfResponse>().WithApiErrors();
        group.MapPost("/login", async (CredentialsRequest request, SessionService sessions, HttpContext context,
            AccountConnections connections, TimeProvider clock, CancellationToken ct) =>
        {
            var session = await sessions.LoginAsync(request.Username, request.Password, ct);
            var previous = await sessions.LogoutAsync(context.Request.Cookies[SessionCookie.Name], ct);
            if (previous is not null) connections.RevokeFamily(previous.FamilyId);
            SessionCookie.Write(context, session, clock);
            return Results.Ok(session.Login);
        }).WithName("Login").Produces<LoginResponse>().WithApiErrors();
        group.MapPost("/refresh", async (SessionService sessions, HttpContext context, AccountConnections connections,
            TimeProvider clock, CancellationToken ct) =>
        {
            var attempt = await sessions.RefreshAsync(context.Request.Cookies[SessionCookie.Name], ct);
            if (attempt.RevokedFamily is { } revoked) connections.RevokeFamily(revoked.FamilyId);
            if (attempt.Session is not { } session)
            {
                SessionCookie.Clear(context);
                await ApiError.WriteAsync(context, 401, "session_expired", "Your session expired or was revoked. Please log in again.");
                return Results.Empty;
            }
            SessionCookie.Write(context, session, clock);
            return Results.Ok(session.Login);
        }).WithName("RefreshSession").Produces<LoginResponse>().WithApiErrors();
        group.MapPost("/logout", async (SessionService sessions, HttpContext context, AccountConnections connections, CancellationToken ct) =>
        {
            var revoked = await sessions.LogoutAsync(context.Request.Cookies[SessionCookie.Name], ct);
            if (revoked is not null) connections.RevokeFamily(revoked.FamilyId);
            SessionCookie.Clear(context);
            return Results.NoContent();
        }).WithName("LogoutSession").Produces(204).WithApiErrors();
        return endpoints;
    }
}
