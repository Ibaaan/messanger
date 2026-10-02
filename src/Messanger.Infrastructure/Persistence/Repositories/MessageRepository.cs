using Messanger.Application;
using Messanger.Application.Persistence;
using Messanger.Application.Messages;
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

    private IQueryable<MessageResponse> Responses =>
        from message in db.Messages.AsNoTracking()
        join user in db.Users on message.SenderId equals user.Id
        join profile in db.Set<UserProfile>() on user.Id equals profile.UserId
        select new MessageResponse(message.Id, message.ChatId, user.Id, user.Username,
            profile.DisplayName, message.Text, message.CreatedAt, message.ReplyToMessageId);

    public async Task<IReadOnlyList<MessageResponse>> GetHistoryResponsesAsync(long chatId, long? beforeId, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 50);
        return await Responses.Where(message => message.ChatId == chatId && (beforeId == null || message.Id < beforeId))
            .OrderByDescending(message => message.Id).Take(limit).ToListAsync(cancellationToken);
    }

    public async Task<MessageResponse> SaveTextAsync(long chatId, long senderId, string text, long? replyToMessageId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Lock the rows whose state authorizes this write, so a concurrent suspension/leave cannot race the insert.
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE id = {senderId} AND account_status = 'ACTIVE' FOR SHARE")
            .SingleOrDefaultAsync(cancellationToken);
        var member = await db.Participants.FromSqlInterpolated($"SELECT * FROM chat_participants WHERE chat_id = {chatId} AND user_id = {senderId} AND left_at IS NULL FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null || member is null)
            throw new UseCaseException("forbidden", "You are not an active member of this chat.");
        if (replyToMessageId is not null && !await db.Messages.AnyAsync(message => message.Id == replyToMessageId && message.ChatId == chatId, cancellationToken))
            throw new UseCaseException("validation_failed", "The replied-to message must belong to this chat.");
        var message = new Message { ChatId = chatId, SenderId = senderId, Text = text, ReplyToMessageId = replyToMessageId };
        db.Messages.Add(message);
        await db.SaveChangesAsync(cancellationToken);
        var response = await Responses.SingleAsync(item => item.Id == message.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    public void Add(Message message)
    {
        db.Messages.Add(message);
    }
}