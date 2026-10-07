using Messanger.Application.Persistence;

namespace Messanger.Application.Chats;

public sealed class ChatAccessService(IUserRepository users, IChatRepository chats)
{
    public async Task RequireActiveAsync(long userId, CancellationToken cancellationToken)
    {
        if ((await users.GetByIdAsync(userId, cancellationToken))?.AccountStatus != "ACTIVE")
            throw new UseCaseException("forbidden", "An active account is required.");
    }

    public async Task RequireMemberAsync(long chatId, long userId, CancellationToken cancellationToken)
    {
        ChatMembershipRules.RequirePositiveId(chatId);
        await RequireActiveAsync(userId, cancellationToken);
        if (!await chats.IsActiveParticipantAsync(chatId, userId, cancellationToken))
            throw new UseCaseException("forbidden", "You are not an active member of this chat.");
    }
}
