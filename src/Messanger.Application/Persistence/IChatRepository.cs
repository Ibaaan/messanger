using Messanger.Domain;
using Messanger.Application.Chats;

namespace Messanger.Application.Persistence;

public interface IChatRepository
{
    Task<Chat?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Chat>> GetForUserAsync(long userId, CancellationToken cancellationToken = default);
    Task<bool> IsActiveParticipantAsync(long chatId, long userId, CancellationToken cancellationToken = default);
    Task CreateAsync(Chat chat, long ownerId, IReadOnlyCollection<long> userIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatMemberResponse>> GetMembersAsync(long chatId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<long>> GetActiveUserIdsAsync(long chatId, CancellationToken cancellationToken = default);
    void Add(Chat chat);
    void AddParticipant(ChatParticipant participant);
}