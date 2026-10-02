namespace Messanger.Chats;

public sealed record CreateChatRequest(string? ChatName, string? Description, long[]? UserIds);
