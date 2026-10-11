using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using Messanger.Application.Persistence;
using Microsoft.AspNetCore.Authorization;

namespace Messanger.Authentication;

internal sealed class ActiveAccountHandler(IUserRepository users, IHttpContextAccessor contexts)
    : AuthorizationHandler<ActiveAccountRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,
        ActiveAccountRequirement requirement)
    {
        var subject = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (!long.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out var userId) || userId <= 0)
            return;

        var cancellationToken = contexts.HttpContext?.RequestAborted ?? default;
        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (AccountSession.IsCurrent(user, context.User))
            context.Succeed(requirement);
    }
}