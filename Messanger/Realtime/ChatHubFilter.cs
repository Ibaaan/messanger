using Messanger.Application;
using Microsoft.AspNetCore.SignalR;

namespace Messanger.Realtime;

internal sealed class ChatHubFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocation,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try { return await next(invocation); }
        catch (UseCaseException exception) { throw new HubException(exception.Code + ": " + exception.Message); }
    }
}
