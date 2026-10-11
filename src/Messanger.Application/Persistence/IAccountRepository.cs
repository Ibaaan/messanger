using Messanger.Application.Accounts;

namespace Messanger.Application.Persistence;

public interface IAccountRepository
{
    // Owns the transaction: status/version change and refresh-family revocation commit together.
    Task<AccountStatusChange> ChangeStatusAsync(long actorId, long targetId, string status, CancellationToken ct);
    Task DeleteSelfAsync(long userId, CancellationToken ct);
}
