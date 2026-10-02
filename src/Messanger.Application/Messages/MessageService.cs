using Messanger.Application.Chats;
using Messanger.Application.Persistence;

namespace Messanger.Application.Messages;

public sealed class MessageService(IMessageRepository messages, ChatAccessService access)
{
    public async Task<IReadOnlyList<MessageResponse>> HistoryAsync(long chatId, long callerId, long? beforeId,
        int limit, CancellationToken cancellationToken)
    {
        await access.RequireMemberAsync(chatId, callerId, cancellationToken);
        if (limit is < 1 or > 50 || beforeId is <= 0)
            throw new UseCaseException("validation_failed", "History limits must be 1–50 and cursors must be positive message IDs.");
        var history = await messages.GetHistoryResponsesAsync(chatId, beforeId, limit, cancellationToken);
        return history.Reverse().ToArray();
    }

    public async Task<MessageResponse> SendAsync(long chatId, long callerId, string? text,
        long? replyToMessageId, CancellationToken cancellationToken)
    {
        await access.RequireMemberAsync(chatId, callerId, cancellationToken);
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4000 || replyToMessageId is <= 0)
            throw new UseCaseException("validation_failed", "A text message must contain 1–4000 characters and a reply ID must be positive.");
        return await messages.SaveTextAsync(chatId, callerId, text, replyToMessageId, cancellationToken);
    }
}
