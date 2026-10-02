using Messanger.Application.Persistence;
using Messanger.Application.Chats;
using Messanger.Domain;
using Microsoft.EntityFrameworkCore;

namespace Messanger.Infrastructure.Persistence.Repositories;

internal sealed class ChatRepository(MessangerDbContext db) : IChatRepository
{
    public Task<Chat?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        return db.Chats.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Chat>>
        GetForUserAsync(long userId, CancellationToken cancellationToken = default)
    {
        return await db.Chats
            .Where(chat => db.Participants.Any(p => p.ChatId == chat.Id && p.UserId == userId && p.LeftAt == null))
            .OrderByDescending(x => x.Id).ToListAsync(cancellationToken);
    }

    public Task<bool>
        IsActiveParticipantAsync(long chatId, long userId, CancellationToken cancellationToken = default)
    {
        return db.Participants.AnyAsync(x => x.ChatId == chatId && x.UserId == userId && x.LeftAt == null,
            cancellationToken);
    }

    public async Task CreateAsync(Chat chat, long ownerId, IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Chats.Add(chat);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var userId in userIds.Append(ownerId).Distinct())
            db.Participants.Add(new ChatParticipant
            {
                ChatId = chat.Id, UserId = userId, Role = userId == ownerId ? "OWNER" : "MEMBER"
            });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChatMemberResponse>> GetMembersAsync(long chatId, CancellationToken cancellationToken = default) =>
        await (from member in db.Participants.AsNoTracking()
               join user in db.Users on member.UserId equals user.Id
               join profile in db.Set<UserProfile>() on user.Id equals profile.UserId
               where member.ChatId == chatId && member.LeftAt == null
               orderby member.Id
               select new ChatMemberResponse(user.Id, user.Username, profile.DisplayName, member.Role))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<long>> GetActiveUserIdsAsync(long chatId, CancellationToken cancellationToken = default) =>
        await (from member in db.Participants.AsNoTracking()
               join user in db.Users on member.UserId equals user.Id
               where member.ChatId == chatId && member.LeftAt == null && user.AccountStatus == "ACTIVE"
               select user.Id).ToListAsync(cancellationToken);

    public void Add(Chat chat)
    {
        db.Chats.Add(chat);
    }

    public void AddParticipant(ChatParticipant participant)
    {
        db.Participants.Add(participant);
    }
}