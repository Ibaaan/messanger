using Messanger.Application.Persistence;
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

    public void Add(Chat chat)
    {
        db.Chats.Add(chat);
    }

    public void AddParticipant(ChatParticipant participant)
    {
        db.Participants.Add(participant);
    }
}