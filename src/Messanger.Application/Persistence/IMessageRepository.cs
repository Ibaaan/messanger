using Messanger.Domain;
using Messanger.Application.Messages;

namespace Messanger.Application.Persistence;

public interface IMessageRepository
{
    Task<Message?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Message>> GetHistoryAsync(long chatId, long? beforeId = null, int limit = 50,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MessageResponse>> GetHistoryResponsesAsync(long chatId, long? beforeId, int limit, CancellationToken cancellationToken = default);
    Task<MessageResponse> SaveTextAsync(long chatId, long senderId, string text, long? replyToMessageId, CancellationToken cancellationToken = default);
    void Add(Message message);
}