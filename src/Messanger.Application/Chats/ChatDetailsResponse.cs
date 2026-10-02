using Messanger.Application.Messages;

namespace Messanger.Application.Chats;

public sealed record ChatDetailsResponse(ChatResponse Chat, IReadOnlyList<MessageResponse> Messages);
