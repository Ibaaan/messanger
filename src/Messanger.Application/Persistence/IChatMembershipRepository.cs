namespace Messanger.Application.Persistence;

// Each operation validates permissions against locked database state and commits atomically.
public interface IChatMembershipRepository
{
    Task AddMemberAsync(long chatId, long callerId, long userId, CancellationToken cancellationToken);
    Task RemoveMemberAsync(long chatId, long callerId, long userId, CancellationToken cancellationToken);
    Task LeaveAsync(long chatId, long callerId, CancellationToken cancellationToken);
    Task TransferOwnershipAsync(long chatId, long callerId, long userId, CancellationToken cancellationToken);
    Task ChangeRoleAsync(long chatId, long callerId, long userId, string role, CancellationToken cancellationToken);
    Task UpdateDetailsAsync(long chatId, long callerId, string name, string? description, CancellationToken cancellationToken);
}
