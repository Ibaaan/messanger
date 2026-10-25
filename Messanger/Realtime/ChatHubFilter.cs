using Messanger.Application;
using Messanger.Security;
using Messanger.Authentication;
using Messanger.Chats;
using Microsoft.AspNetCore.SignalR;

namespace Messanger.Realtime;

internal sealed class ChatHubFilter(AccountSession session, SecurityBudgets budgets, SecurityAudit audit) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocation,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            var userId = ChatEndpoints.UserId(invocation.Context.User!);
            var key = SecurityBudgets.UserKey(userId);
            using var calls = budgets.Acquire("hub", key);
            if (!calls.IsAcquired)
            {
                audit.RateLimited(invocation.Context.GetHttpContext()!, "hub_calls", userId);
                invocation.Context.Abort();
                throw new HubException("rate_limited: Too many hub calls. Reconnect later.");
            }
            if (invocation.HubMethodName is "SendMessage" or "SendReply")
            {
                using var messages = budgets.Acquire("messages", key);
                if (!messages.IsAcquired)
                {
                    audit.RateLimited(invocation.Context.GetHttpContext()!, "messages", userId);
                    throw new HubException("rate_limited: Too many messages. Try again later.");
                }
            }
            if (!await session.IsCurrentAsync(invocation.Context.User!, invocation.Context.ConnectionAborted))
            {
                invocation.Context.Abort();
                throw new UseCaseException("forbidden", "Your session is no longer active.");
            }
            return await next(invocation);
        }
        catch (UseCaseException exception) { throw new HubException(exception.Code + ": " + exception.Message); }
    }
}
