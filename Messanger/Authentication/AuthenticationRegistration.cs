using System.Globalization;
using Messanger.Security;
using System.IdentityModel.Tokens.Jwt;
using Messanger.Application.Authentication;
using Messanger.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Messanger.Authentication;

internal static class AuthenticationRegistration
{
    public static IServiceCollection AddAccessAuthentication(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAuthenticationInfrastructure(configuration);
        services.AddScoped<AuthenticationService>();
        services.AddHttpContextAccessor();
        services.AddScoped<IAuthorizationHandler, ActiveAccountHandler>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<JwtSigningKey, IOptions<JwtOptions>>((options, key, settings) =>
            {
                options.MapInboundClaims = false;
                options.IncludeErrorDetails = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = settings.Value.Issuer,
                    ValidateAudience = true,
                    ValidAudience = settings.Value.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeys = key.ValidationKeys,
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(60),
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    ValidTypes = ["at+jwt"],
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                    RoleClaimType = "role"
                };
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = context =>
                    {
                        context.HttpContext.RequestServices.GetRequiredService<SecurityAudit>().AuthenticationFailure(context.HttpContext, "access_token_rejected");
                        return Task.CompletedTask;
                    },
                    OnForbidden = context =>
                    {
                        context.HttpContext.RequestServices.GetRequiredService<SecurityAudit>().AccessDenied(context.HttpContext);
                        return Task.CompletedTask;
                    },
                    OnMessageReceived = context =>
                    {
                        var request = context.Request;
                        // Browsers cannot set Authorization on WebSocket/SSE requests. Restrict the query token to the hub.
                        if (request.Path == "/hubs/chat" && request.Query["access_token"].Count == 1
                            && (request.HttpContext.WebSockets.IsWebSocketRequest || request.Headers.Accept.ToString().Contains("text/event-stream", StringComparison.Ordinal)))
                            context.Token = request.Query["access_token"];
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        var principal = context.Principal!;
                        var subject = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                        var issuedAt = principal.FindFirst(JwtRegisteredClaimNames.Iat)?.Value;
                        if (principal.FindFirst("token_use")?.Value != "access"
                            || !long.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ||
                            id <= 0
                            || !long.TryParse(issuedAt, NumberStyles.None, CultureInfo.InvariantCulture, out _)
                            || string.IsNullOrWhiteSpace(principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value)
                            || string.IsNullOrWhiteSpace(principal.FindFirst("role")?.Value)
                            || !Guid.TryParse(principal.FindFirst("sid")?.Value, out var family) || family == Guid.Empty
                            || !int.TryParse(principal.FindFirst("session_version")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 0)
                            context.Fail("Invalid access token.");
                        return Task.CompletedTask;
                    }
                };
            });
        services.AddAuthorization(options =>
        {
            var policy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser().AddRequirements(new ActiveAccountRequirement()).Build();
            options.DefaultPolicy = policy;
            options.FallbackPolicy = policy;
        });
        return services;
    }
}