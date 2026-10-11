using Messanger.Application.Persistence;

namespace Messanger.Application.Accounts;

public sealed class AccountService(IAccountRepository accounts)
{
    public Task<AccountStatusChange> ChangeStatusAsync(long actorId, long targetId, string? status, CancellationToken ct)
    {
        if (targetId <= 0 || status is not ("ACTIVE" or "SUSPENDED"))
            throw new UseCaseException("validation_failed", "Use a positive user ID and status ACTIVE or SUSPENDED.");
        if (actorId == targetId)
            throw new UseCaseException("validation_failed", "Administrators cannot change their own status.");
        return accounts.ChangeStatusAsync(actorId, targetId, status, ct);
    }

    public Task DeleteSelfAsync(long userId, CancellationToken ct) => accounts.DeleteSelfAsync(userId, ct);
}
