using Messanger.Application.Messages;

namespace Messanger.Application.Chats;

public sealed record ChatUpdateResponse(long ChatId, MessageResponse? LastMessage, string? MessageType,
    long UnreadCount, long? LastReadMessageId);
