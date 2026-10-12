using Messanger.Application;
using Messanger.Application.Persistence;
using Messanger.Authentication;
using Messanger.Chats;
using Microsoft.AspNetCore.SignalR;

namespace Messanger.Realtime;

internal sealed class ChatHubFilter(IUserRepository users) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocation,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            var user = await users.GetByIdAsync(ChatEndpoints.UserId(invocation.Context.User!), invocation.Context.ConnectionAborted);
            if (!AccountSession.IsCurrent(user, invocation.Context.User!))
            {
                invocation.Context.Abort();
                throw new UseCaseException("forbidden", "Your session is no longer active.");
            }
            return await next(invocation);
        }
        catch (UseCaseException exception) { throw new HubException(exception.Code + ": " + exception.Message); }
    }
}
