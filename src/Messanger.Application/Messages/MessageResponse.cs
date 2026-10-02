namespace Messanger.Application.Messages;

public sealed record MessageResponse(long Id, long ChatId, long SenderId, string SenderUsername,
    string SenderDisplayName, string Text, DateTimeOffset Timestamp, long? ReplyToMessageId);
