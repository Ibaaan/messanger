namespace Messanger.Application.Chats;

public sealed record ChatMemberResponse(long UserId, string Username, string DisplayName, string Role);
