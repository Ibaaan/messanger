using Messanger.Application.Persistence;
using Messanger.Domain;
using Microsoft.EntityFrameworkCore;

namespace Messanger.Infrastructure.Persistence.Repositories;

internal sealed class MessageRepository(MessangerDbContext db) : IMessageRepository
{
    public Task<Message?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        return db.Messages.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Message>> GetHistoryAsync(long chatId, long? beforeId = null, int limit = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 100);
        return await db.Messages.AsNoTracking().Where(x => x.ChatId == chatId && (beforeId == null || x.Id < beforeId))
            .OrderByDescending(x => x.Id).Take(limit).ToListAsync(cancellationToken);
    }

    public void Add(Message message)
    {
        db.Messages.Add(message);
    }
}