using Messanger.Application;
using Messanger.Application.Accounts;
using Messanger.Application.Persistence;
using Messanger.Domain;
using Microsoft.EntityFrameworkCore;

namespace Messanger.Infrastructure.Persistence.Repositories;

internal sealed class AccountRepository(MessangerDbContext db, TimeProvider clock) : IAccountRepository
{
    public async Task<AccountStatusChange> ChangeStatusAsync(long actorId, long targetId, string status, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Serialize rare account lifecycle changes, including concurrent deletion of the last administrators.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73642109)", ct);
        var users = await LockUsersAsync(actorId, targetId, ct);
        var actor = users.GetValueOrDefault(actorId);
        if (actor is not { AccountStatus: "ACTIVE", Role: "ADMIN" })
            throw new UseCaseException("forbidden", "An active administrator is required.");
        var target = users.GetValueOrDefault(targetId) ?? throw new UseCaseException("not_found", "Account not found.");
        if (target.AccountStatus == "DELETED")
            throw new UseCaseException("account_conflict", "Deleted accounts cannot be restored.");
        if (actorId == targetId || status is not ("ACTIVE" or "SUSPENDED"))
            throw new UseCaseException("validation_failed", "Invalid status change.");
        await SetStatusAsync(target, status, ct);
        await transaction.CommitAsync(ct);
        return new AccountStatusChange(new AccountResponse(target.Id, target.Username, target.Role, target.AccountStatus), target.SessionVersion);
    }

    public async Task<int> ChangePasswordAsync(long userId, int expectedSessionVersion, string expectedPasswordHash,
        string replacementPasswordHash, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // PostgreSQL rechecks the predicate after waiting for a concurrent update. A stale verification cannot win twice.
        var changed = await db.Users.Where(user => user.Id == userId && user.AccountStatus == "ACTIVE"
                && user.SessionVersion == expectedSessionVersion && user.PasswordHash == expectedPasswordHash)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.PasswordHash, replacementPasswordHash)
                .SetProperty(user => user.SessionVersion, user => user.SessionVersion + 1), ct);
        if (changed != 1)
            throw new UseCaseException("account_conflict", "Your account changed. Sign in again before changing your password.");
        // The user row remains locked until commit, serializing this against refresh and account lifecycle changes.
        await db.RefreshTokens.Where(token => token.UserId == userId && token.RevokedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(token => token.RevokedAt, clock.GetUtcNow()), ct);
        await transaction.CommitAsync(ct);
        return expectedSessionVersion + 1;
    }

    public async Task DeleteSelfAsync(long userId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Serialize rare account lifecycle changes, including concurrent deletion of the last administrators.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73642109)", ct);
        var users = await LockUsersAsync(userId, userId, ct);
        var user = users.GetValueOrDefault(userId);
        if (user is not { AccountStatus: "ACTIVE" })
            throw new UseCaseException("forbidden", "An active account is required.");
        // Ownership transfer locks this same user first, so deletion cannot race a new ownership assignment.
        if (await db.Participants.AnyAsync(member => member.UserId == userId && member.LeftAt == null && member.Role == "OWNER", ct))
            throw new UseCaseException("owner_transfer_required", "Transfer ownership of your groups before deleting your account.");
        if (user.Role == "ADMIN" && !await db.Users.AnyAsync(other => other.Id != userId && other.Role == "ADMIN" && other.AccountStatus == "ACTIVE", ct))
            throw new UseCaseException("account_conflict", "The last active administrator cannot delete their account.");
        await SetStatusAsync(user, "DELETED", ct);
        // Preserve membership and message rows for history, but departed users no longer occupy group capacity.
        await db.Participants.Where(member => member.UserId == userId && member.LeftAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(member => member.LeftAt, clock.GetUtcNow()), ct);
        await transaction.CommitAsync(ct);
    }

    private async Task<Dictionary<long, User>> LockUsersAsync(long actorId, long targetId, CancellationToken ct)
    {
        var result = new Dictionary<long, User>();
        foreach (var id in new[] { actorId, targetId }.Distinct().Order())
        {
            // No tracking: authorization must use current locked state, even in a reused DI scope.
            var user = await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE id = {id} FOR UPDATE")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            if (user is not null) result.Add(id, user);
        }
        return result;
    }

    private async Task SetStatusAsync(User user, string status, CancellationToken ct)
    {
        if (user.AccountStatus == status) return;
        var now = clock.GetUtcNow();
        await db.Users.Where(item => item.Id == user.Id).ExecuteUpdateAsync(update => update
            .SetProperty(item => item.AccountStatus, status)
            .SetProperty(item => item.SessionVersion, item => item.SessionVersion + 1), ct);
        await db.RefreshTokens.Where(token => token.UserId == user.Id && token.RevokedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(token => token.RevokedAt, now), ct);
        user.AccountStatus = status;
        user.SessionVersion++;
    }
}
