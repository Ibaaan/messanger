using Messanger.Chats;

namespace Messanger.Security;

internal sealed class PasswordAttemptFilter(SecurityBudgets budgets) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var userId = ChatEndpoints.UserId(context.HttpContext.User);
        using var lease = budgets.Acquire("password", SecurityBudgets.UserKey(userId));
        return lease.IsAcquired ? await next(context) : await SecurityControls.RejectAsync(context.HttpContext, lease, "password_user");
    }
}
