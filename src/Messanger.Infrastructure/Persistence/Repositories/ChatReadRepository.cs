using Messanger.Application;
using Messanger.Application.Chats;
using Messanger.Application.Persistence;
using Messanger.Domain;
using Microsoft.EntityFrameworkCore;

namespace Messanger.Infrastructure.Persistence.Repositories;

internal sealed class ChatReadRepository(MessangerDbContext db, TimeProvider clock) : IChatReadRepository
{
    public async Task<ChatReadState> GetAsync(long chatId, long userId, CancellationToken ct)
    {
        // One SQL snapshot keeps the marker and its correlated count consistent with each other.
        var state = await (from participant in db.Participants.AsNoTracking()
                           join read in db.Set<ChatLastRead>().AsNoTracking() on participant.Id equals read.ChatParticipantId into markers
                           from read in markers.DefaultIfEmpty()
                           where participant.ChatId == chatId && participant.UserId == userId && participant.LeftAt == null
                           select new ChatReadState(chatId, read == null ? null : read.LastReadMessageId,
                               db.Messages.LongCount(message => message.ChatId == chatId &&
                                   (read == null || read.LastReadMessageId == null || message.Id > read.LastReadMessageId))))
            .SingleOrDefaultAsync(ct);
        return state ?? throw new UseCaseException("forbidden", "You are not an active member of this chat.");
    }

    public async Task<ChatReadState> AdvanceAsync(long chatId, long userId, long messageId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Match send/membership lock order. No tracked snapshot may authorize a read after removal/suspension.
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE id = {userId} FOR SHARE")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        var participant = await db.Participants.FromSqlInterpolated($"SELECT * FROM chat_participants WHERE chat_id = {chatId} AND user_id = {userId} AND left_at IS NULL FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (user is not { AccountStatus: "ACTIVE" } || participant is null)
            throw new UseCaseException("forbidden", "You are not an active member of this chat.");
        if (!await db.Messages.AnyAsync(message => message.Id == messageId && message.ChatId == chatId, ct))
            throw new UseCaseException("validation_failed", "The read message must exist in this chat.");

        var marker = await db.Set<ChatLastRead>().AsNoTracking()
            .SingleOrDefaultAsync(read => read.ChatParticipantId == participant.Id, ct);
        if (marker is null)
        {
            db.Set<ChatLastRead>().Add(new ChatLastRead
            {
                ChatParticipantId = participant.Id, LastReadMessageId = messageId, LastReadAt = clock.GetUtcNow()
            });
            await db.SaveChangesAsync(ct);
        }
        else if (marker.LastReadMessageId is null || marker.LastReadMessageId < messageId)
            await db.Set<ChatLastRead>().Where(read => read.Id == marker.Id).ExecuteUpdateAsync(update => update
                .SetProperty(read => read.LastReadMessageId, messageId).SetProperty(read => read.LastReadAt, clock.GetUtcNow()), ct);
        // Backward/repeated acknowledgements leave both the marker and timestamp unchanged.
        var result = await GetAsync(chatId, userId, ct);
        await transaction.CommitAsync(ct);
        return result;
    }
}
