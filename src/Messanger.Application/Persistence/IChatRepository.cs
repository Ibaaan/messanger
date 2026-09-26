using Messanger.Domain;

namespace Messanger.Application.Persistence;

public interface IChatRepository
{
    Task<Chat?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Chat>> GetForUserAsync(long userId, CancellationToken cancellationToken = default);
    Task<bool> IsActiveParticipantAsync(long chatId, long userId, CancellationToken cancellationToken = default);
    void Add(Chat chat);
    void AddParticipant(ChatParticipant participant);
}