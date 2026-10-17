using Messanger.Application.Persistence;

namespace Messanger.Application.Chats;

public sealed class ChatReadService(IChatReadRepository reads, ChatAccessService access)
{
    public async Task<ChatReadState> MarkReadAsync(long chatId, long userId, long messageId, CancellationToken ct)
    {
        ChatMembershipRules.RequirePositiveId(chatId);
        ChatMembershipRules.RequirePositiveId(messageId);
        await access.RequireMemberAsync(chatId, userId, ct);
        return await reads.AdvanceAsync(chatId, userId, messageId, ct);
    }
}
