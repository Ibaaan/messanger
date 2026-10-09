using Messanger.Application;
using Messanger.Application.Chats;
using Messanger.Application.Persistence;
using Messanger.Domain;
using Microsoft.EntityFrameworkCore;

namespace Messanger.Infrastructure.Persistence.Repositories;

internal sealed class ChatMembershipRepository(MessangerDbContext db, TimeProvider clock) : IChatMembershipRepository
{
    public Task AddMemberAsync(long chatId, long callerId, long userId, CancellationToken ct) =>
        ChangeAsync(chatId, callerId, userId, async (chat, actor, members) =>
        {
            ChatMembershipRules.RequireOwner(actor);
            await RequireActiveTargetAsync(userId, ct);
            var target = members.SingleOrDefault(member => member.UserId == userId);
            if (target is { LeftAt: null })
                throw new UseCaseException("member_already_active", "This user is already an active participant.");
            if (members.Count(member => member.LeftAt == null) >= ChatMembershipRules.MaximumMembers)
                throw new UseCaseException("membership_conflict", "A group may contain at most 51 active participants.");
            if (target is null)
                db.Participants.Add(new ChatParticipant { ChatId = chatId, UserId = userId });
            else
            {
                // Reuse the unique membership row and reset privileges on rejoin.
                target.LeftAt = null;
                target.JoinedAt = clock.GetUtcNow();
                target.Role = "MEMBER";
            }
        }, ct);

    public Task RemoveMemberAsync(long chatId, long callerId, long userId, CancellationToken ct) =>
        ChangeAsync(chatId, callerId, userId, (chat, actor, members) =>
        {
            var target = members.SingleOrDefault(member => member.UserId == userId)
                ?? throw new UseCaseException("not_found", "Participant not found.");
            ChatMembershipRules.RequireCanRemove(actor, target);
            target.LeftAt ??= clock.GetUtcNow();
            return Task.CompletedTask;
        }, ct);

    public Task LeaveAsync(long chatId, long callerId, CancellationToken ct) =>
        ChangeAsync(chatId, callerId, callerId, (chat, actor, members) =>
        {
            if (actor.LeftAt is not null) return Task.CompletedTask;
            if (actor.Role == "OWNER")
                throw new UseCaseException("owner_transfer_required", "Transfer ownership before leaving the chat.");
            actor.LeftAt = clock.GetUtcNow();
            return Task.CompletedTask;
        }, ct, allowDepartedActor: true);

    public Task TransferOwnershipAsync(long chatId, long callerId, long userId, CancellationToken ct) =>
        ChangeAsync(chatId, callerId, userId, async (chat, actor, members) =>
        {
            ChatMembershipRules.RequireOwner(actor);
            await RequireActiveTargetAsync(userId, ct);
            var target = ActiveTarget(members, userId);
            actor.Role = "MEMBER";
            target.Role = "OWNER";
        }, ct);

    public Task ChangeRoleAsync(long chatId, long callerId, long userId, string role, CancellationToken ct) =>
        ChangeAsync(chatId, callerId, userId, async (chat, actor, members) =>
        {
            ChatMembershipRules.RequireOwner(actor);
            var target = ActiveTarget(members, userId);
            if (target.Role == "OWNER")
                throw new UseCaseException("owner_transfer_required", "Use ownership transfer to change the owner.");
            await RequireActiveTargetAsync(userId, ct);
            target.Role = ChatMembershipRules.ValidateRole(role);
        }, ct);

    public Task UpdateDetailsAsync(long chatId, long callerId, string name, string? description, CancellationToken ct) =>
        ChangeAsync(chatId, callerId, callerId, (chat, actor, members) =>
        {
            ChatMembershipRules.RequireOwner(actor);
            chat.Name = ChatMembershipRules.ValidateName(name, description);
            chat.Description = description;
            return Task.CompletedTask;
        }, ct);

    private async Task ChangeAsync(long chatId, long callerId, long targetId,
        Func<Chat, ChatParticipant, IReadOnlyList<ChatParticipant>, Task> change, CancellationToken ct,
        bool allowDepartedActor = false)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Lock users in a stable order, then the group, then its memberships. Sends lock users before memberships too.
        foreach (var id in new[] { callerId, targetId }.Distinct().Order())
        {
            var user = await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE id = {id} FOR SHARE")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            if (id == callerId && user?.AccountStatus != "ACTIVE")
                throw new UseCaseException("forbidden", "An active account is required.");
        }
        var chatAlreadyTracked = db.Chats.Local.Any(chat => chat.Id == chatId);
        var trackedMemberIds = db.Participants.Local.Where(member => member.ChatId == chatId).Select(member => member.Id).ToHashSet();
        var chat = await db.Chats.FromSqlInterpolated($"SELECT * FROM chats WHERE id = {chatId} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw new UseCaseException("forbidden", "You are not an active member of this chat.");
        if (chatAlreadyTracked) await db.Entry(chat).ReloadAsync(ct);
        if (chat.ChatType != "group") throw new UseCaseException("membership_conflict", "Only group chats support membership management.");
        var members = await db.Participants.FromSqlInterpolated($"SELECT * FROM chat_participants WHERE chat_id = {chatId} ORDER BY user_id FOR UPDATE")
            .ToListAsync(ct);
        // EF identity resolution can return older tracked values; permission checks need the state protected by these locks.
        foreach (var member in members.Where(member => trackedMemberIds.Contains(member.Id)))
            await db.Entry(member).ReloadAsync(ct);
        var actor = members.SingleOrDefault(member => member.UserId == callerId);
        if (actor is null || (!allowDepartedActor && actor.LeftAt is not null))
            throw new UseCaseException("forbidden", "You are not an active member of this chat.");
        await change(chat, actor, members);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task RequireActiveTargetAsync(long userId, CancellationToken ct)
    {
        // AsNoTracking reads current state even if this request already tracked the account.
        if (!await db.Users.AsNoTracking().AnyAsync(user => user.Id == userId && user.AccountStatus == "ACTIVE", ct))
            throw new UseCaseException("validation_failed", "The selected user must be an existing active account.");
    }

    private static ChatParticipant ActiveTarget(IReadOnlyList<ChatParticipant> members, long userId) =>
        members.SingleOrDefault(member => member.UserId == userId && member.LeftAt == null)
        ?? throw new UseCaseException("not_found", "Active participant not found.");
}
