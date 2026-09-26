using Messanger.Domain;

namespace Messanger.Application.Persistence;

public interface IMessageRepository
{
    Task<Message?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Message>> GetHistoryAsync(long chatId, long? beforeId = null, int limit = 50,
        CancellationToken cancellationToken = default);

    void Add(Message message);
}