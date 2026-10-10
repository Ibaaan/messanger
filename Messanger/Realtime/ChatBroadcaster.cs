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

    public async Task MembershipChangedAsync(long chatId, long? revokedUserId, CancellationToken cancellationToken)
    {
        // Revoke in-memory access synchronously, before any cancellable I/O or best-effort notification.
        var affected = revokedUserId is { } userId ? subscriptions.Revoke(chatId, userId) : [];
        try
        {
            foreach (var connectionId in affected)
                await hub.Groups.RemoveFromGroupAsync(connectionId, $"chat:{chatId}", CancellationToken.None);
            if (revokedUserId is { } removed)
                await hub.Clients.User(Id(removed)).SendAsync("ChatAccessRevoked", chatId, CancellationToken.None);
            var members = await chats.GetActiveUserIdsAsync(chatId, cancellationToken);
            await hub.Clients.Users(members.Select(Id).ToArray()).SendAsync("ChatMembershipChanged", chatId, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Chat {ChatId} membership committed but notification failed", chatId);
        }
    }

    private static string Id(long id) => id.ToString(CultureInfo.InvariantCulture);
}
