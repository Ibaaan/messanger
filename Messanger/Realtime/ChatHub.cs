using Messanger.Application;
using Messanger.Application.Chats;
using Messanger.Application.Messages;
using Messanger.Chats;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Messanger.Realtime;

[Authorize]
internal sealed class ChatHub(ChatAccessService access, MessageService messages,
    ChatBroadcaster broadcaster, ChatSubscriptions subscriptions) : Hub
{
    private long UserId => ChatEndpoints.UserId(Context.User!);

    public override async Task OnConnectedAsync()
    {
        await access.RequireActiveAsync(UserId, Context.ConnectionAborted);
        subscriptions.Connect(Context.ConnectionId, UserId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        subscriptions.Disconnect(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinChat(long chatId)
    {
        // Capture the fence before checking the DB, so an in-flight join cannot resurrect a revoked subscription.
        var version = subscriptions.Version(chatId, UserId);
        await access.RequireMemberAsync(chatId, UserId, Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"chat:{chatId}", Context.ConnectionAborted);
        try
        {
            if (!subscriptions.Join(Context.ConnectionId, chatId, version))
                throw new UseCaseException("forbidden", "Chat access changed while joining. Check membership and try again.");
            await access.RequireMemberAsync(chatId, UserId, Context.ConnectionAborted);
        }
        catch
        {
            subscriptions.Leave(Context.ConnectionId, chatId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat:{chatId}");
            throw;
        }
    }

    public async Task LeaveChat(long chatId)
    {
        ChatMembershipRules.RequirePositiveId(chatId);
        await access.RequireActiveAsync(UserId, Context.ConnectionAborted);
        subscriptions.Leave(Context.ConnectionId, chatId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat:{chatId}", Context.ConnectionAborted);
    }

    public Task<MessageResponse> SendMessage(long chatId, string? text) => SendAsync(chatId, text, null);
    public Task<MessageResponse> SendReply(long chatId, string? text, long replyToMessageId) => SendAsync(chatId, text, replyToMessageId);

    private async Task<MessageResponse> SendAsync(long chatId, string? text, long? replyToMessageId)
    {
        var message = await messages.SendAsync(chatId, UserId, text, replyToMessageId, Context.ConnectionAborted);
        await broadcaster.MessageAsync(message, Context.ConnectionAborted);
        return message;
    }
}
