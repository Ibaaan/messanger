using Messanger.Application.Authentication;
using Messanger.Application.Persistence;

namespace Messanger.Application.Accounts;

public sealed class PasswordChangeService(IUserRepository users, IPasswordService passwords, IAccountRepository accounts)
{
    public async Task<int> ChangeAsync(long userId, int sessionVersion, string? currentPassword, string? newPassword, CancellationToken ct)
    {
        currentPassword = PasswordRules.Validate(currentPassword);
        newPassword = PasswordRules.Validate(newPassword);
        var user = await users.GetByIdAsync(userId, ct);
        if (user is not { AccountStatus: "ACTIVE" } || user.SessionVersion != sessionVersion)
            throw new UseCaseException("forbidden", "Your session is no longer active.");
        if (!passwords.Verify(user, currentPassword))
            throw new UseCaseException("invalid_credentials", "The current password is incorrect.");
        // Hash outside the transaction; compare the verified hash/version again atomically when saving.
        var replacementHash = passwords.Hash(user, newPassword);
        return await accounts.ChangePasswordAsync(userId, sessionVersion, user.PasswordHash, replacementHash, ct);
    }
}
