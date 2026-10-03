using System.Globalization;
using Messanger.Application.Chats;
using Messanger.Application.Messages;
using Messanger.Application.Persistence;
using Microsoft.AspNetCore.SignalR;

namespace Messanger.Realtime;

internal sealed class ChatBroadcaster(IHubContext<ChatHub> hub, IChatRepository chats,
    ChatSubscriptions subscriptions, ILogger<ChatBroadcaster> logger)
{
    public async Task CreatedAsync(ChatResponse chat, CancellationToken cancellationToken)
    {
        try
        {
            var members = await chats.GetActiveUserIdsAsync(chat.Id, cancellationToken);
            await hub.Clients.Users(members.Select(Id).ToArray()).SendAsync("ChatCreated", chat, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Chat {ChatId} committed but notification failed", chat.Id);
        }
    }

    public async Task MessageAsync(MessageResponse message, CancellationToken cancellationToken)
    {
        try
        {
            // Check membership/status at delivery time too: an old subscription never grants continuing access.
            var members = await chats.GetActiveUserIdsAsync(message.ChatId, cancellationToken);
            var connections = subscriptions.ActiveConnections(message.ChatId, members);
            await hub.Clients.Clients(connections).SendAsync("ReceiveMessage", message, cancellationToken);
            await hub.Clients.Users(members.Select(Id).ToArray()).SendAsync("ChatUpdated", message, cancellationToken);
        }
        catch (Exception exception)
        {
            // The message has already committed and remains recoverable through history.
            logger.LogWarning(exception, "Message {MessageId} committed but notification failed", message.Id);
        }
    }

    private static string Id(long id) => id.ToString(CultureInfo.InvariantCulture);
}
