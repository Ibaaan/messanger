using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Messanger.Application;
using Messanger.Http;
using Messanger.Application.Authentication;
using Messanger.Application.Persistence;

namespace Messanger.Authentication;

internal static class AuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapAuthenticationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/registration",
            async (CredentialsRequest request, AuthenticationService authentication,
                CancellationToken cancellationToken) =>
            {
                var user = await authentication.RegisterAsync(request.Username, request.Password, cancellationToken);
                return Results.Json(user, statusCode: StatusCodes.Status201Created);
            }).AllowAnonymous().WithTags("Authentication").WithName("Register").Produces<UserResponse>(StatusCodes.Status201Created).WithApiErrors();

        // A minimal protected endpoint for the signed-in account; never returns persistence entities or hashes.
        endpoints.MapGet("/api/users/me",
            async (ClaimsPrincipal principal, IUserRepository users, CancellationToken cancellationToken) =>
            {
                var userId = long.Parse(principal.FindFirst(JwtRegisteredClaimNames.Sub)!.Value,
                    CultureInfo.InvariantCulture);
                var user = await users.GetByIdAsync(userId, cancellationToken);
                return user is { AccountStatus: "ACTIVE" } ? Results.Ok(UserResponse.From(user)) : Results.Forbid();
            }).RequireAuthorization().WithTags("Users").WithName("GetCurrentUser").Produces<UserResponse>().WithApiErrors();
        return endpoints;
    }
}