using Messanger.Application.Persistence;

namespace Messanger.Application.Chats;

public sealed class ChatMembershipService(IChatMembershipRepository membership, ChatAccessService access, ChatService chats)
{
    public async Task<ChatResponse> AddAsync(long chatId, long callerId, long userId, CancellationToken ct)
    {
        await ValidateAsync(chatId, callerId, userId, ct);
        await membership.AddMemberAsync(chatId, callerId, userId, ct);
        return (await chats.GetAsync(chatId, callerId, ct)).Chat;
    }

    public async Task RemoveAsync(long chatId, long callerId, long userId, CancellationToken ct)
    {
        await ValidateAsync(chatId, callerId, userId, ct);
        if (callerId == userId) throw new UseCaseException("validation_failed", "Use the leave endpoint to leave a chat.");
        await membership.RemoveMemberAsync(chatId, callerId, userId, ct);
    }

    public async Task LeaveAsync(long chatId, long callerId, CancellationToken ct)
    {
        ChatMembershipRules.RequirePositiveId(chatId);
        await access.RequireActiveAsync(callerId, ct);
        await membership.LeaveAsync(chatId, callerId, ct);
    }

    public async Task<ChatResponse> TransferAsync(long chatId, long callerId, long userId, CancellationToken ct)
    {
        await ValidateAsync(chatId, callerId, userId, ct);
        if (callerId == userId) throw new UseCaseException("validation_failed", "Choose another active participant as the new owner.");
        await membership.TransferOwnershipAsync(chatId, callerId, userId, ct);
        return (await chats.GetAsync(chatId, callerId, ct)).Chat;
    }

    public async Task<ChatResponse> ChangeRoleAsync(long chatId, long callerId, long userId, string? role, CancellationToken ct)
    {
        await ValidateAsync(chatId, callerId, userId, ct);
        await membership.ChangeRoleAsync(chatId, callerId, userId, ChatMembershipRules.ValidateRole(role), ct);
        return (await chats.GetAsync(chatId, callerId, ct)).Chat;
    }

    public async Task<ChatResponse> UpdateAsync(long chatId, long callerId, string? name, string? description, CancellationToken ct)
    {
        ChatMembershipRules.RequirePositiveId(chatId);
        await access.RequireMemberAsync(chatId, callerId, ct);
        await membership.UpdateDetailsAsync(chatId, callerId, ChatMembershipRules.ValidateName(name, description), description, ct);
        return (await chats.GetAsync(chatId, callerId, ct)).Chat;
    }

    private async Task ValidateAsync(long chatId, long callerId, long userId, CancellationToken ct)
    {
        ChatMembershipRules.RequirePositiveId(chatId);
        ChatMembershipRules.RequirePositiveId(userId);
        await access.RequireMemberAsync(chatId, callerId, ct);
    }
}
