using Microsoft.AspNetCore.Authorization;

namespace Messanger.Authentication;

internal sealed class ActiveAccountHandler(AccountSession session, IHttpContextAccessor contexts)
    : AuthorizationHandler<ActiveAccountRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ActiveAccountRequirement requirement)
    {
        if (await session.IsCurrentAsync(context.User, contexts.HttpContext?.RequestAborted ?? default))
            context.Succeed(requirement);
    }
}
