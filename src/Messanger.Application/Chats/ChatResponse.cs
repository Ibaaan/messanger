using Messanger.Application.Messages;

namespace Messanger.Application.Chats;

public sealed record ChatResponse(long Id, string Name, string? Description, DateTimeOffset CreatedAt,
    IReadOnlyList<ChatMemberResponse> Participants, MessageResponse? LastMessage);
