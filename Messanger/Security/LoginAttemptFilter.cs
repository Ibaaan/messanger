using Messanger.Authentication;

namespace Messanger.Security;

internal sealed class LoginAttemptFilter(SecurityBudgets budgets) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var username = context.Arguments.OfType<CredentialsRequest>().Single().Username;
        // Match the actual login identifier rules; normalization must not depend on account existence.
        var key = username is { Length: >= 3 and <= 20 } && username.All(character => char.IsAsciiLetterOrDigit(character) || character == '_')
            ? username.ToUpperInvariant() : "invalid";
        using var lease = budgets.Acquire("login", key);
        return lease.IsAcquired ? await next(context) : await SecurityControls.RejectAsync(context.HttpContext, lease, "login_account");
    }
}
