namespace Messanger.Application.Chats;

public sealed record ChatReadState(long ChatId, long? LastReadMessageId, long UnreadCount);
