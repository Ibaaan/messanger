using Messanger.Application;
using Messanger.Application.Authentication;
using Messanger.Application.Persistence;
using Messanger.Domain;
using Microsoft.EntityFrameworkCore;

namespace Messanger.Infrastructure.Persistence.Repositories;

internal sealed class RefreshSessionRepository(MessangerDbContext db, TimeProvider clock) : IRefreshSessionRepository
{
    public async Task CreateAsync(User user, RefreshTokenFamily token, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var current = await LockUserAsync(user.Id, ct);
        if (current is not { AccountStatus: "ACTIVE" } || current.SessionVersion != user.SessionVersion)
            throw new UseCaseException("invalid_credentials", "Invalid username or password.");
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<RefreshRotation> RotateAsync(RefreshCredential credential, RefreshTokenFamily successor, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // All refresh, logout, and account changes lock the user before touching family records.
        var user = await LockUserAsync(credential.UserId, ct);
        var token = await FindAsync(credential, ct);
        if (user is not { AccountStatus: "ACTIVE" } || user.SessionVersion != credential.SessionVersion
            || token is null || token.ExpiresAt <= clock.GetUtcNow()) return new(null, false);
        if (token.RevokedAt is not null)
        {
            var reused = token.ReplacedByJtiHash is not null;
            if (reused) await RevokeFamilyAsync(credential.FamilyId, ct);
            await transaction.CommitAsync(ct);
            return new(null, reused);
        }
        var now = clock.GetUtcNow();
        await db.RefreshTokens.Where(item => item.Id == token.Id).ExecuteUpdateAsync(update => update
            .SetProperty(item => item.RevokedAt, now).SetProperty(item => item.ReplacedByJtiHash, successor.JtiHash), ct);
        db.RefreshTokens.Add(successor);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(user, false);
    }

    public async Task<bool> RevokeAsync(RefreshCredential credential, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockUserAsync(credential.UserId, ct);
        if (await FindAsync(credential, ct) is null) return false;
        await RevokeFamilyAsync(credential.FamilyId, ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public Task<bool> IsActiveAsync(long userId, Guid familyId, CancellationToken ct) =>
        db.RefreshTokens.AsNoTracking().AnyAsync(token => token.UserId == userId && token.FamilyId == familyId
            && token.RevokedAt == null && token.ExpiresAt > clock.GetUtcNow(), ct);

    private Task<User?> LockUserAsync(long id, CancellationToken ct) =>
        db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE id = {id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);

    private Task<RefreshTokenFamily?> FindAsync(RefreshCredential credential, CancellationToken ct) =>
        db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(token => token.UserId == credential.UserId
            && token.FamilyId == credential.FamilyId && token.JtiHash == credential.JtiHash && token.ExpiresAt == credential.ExpiresAt, ct);

    private Task<int> RevokeFamilyAsync(Guid familyId, CancellationToken ct) =>
        db.RefreshTokens.Where(token => token.FamilyId == familyId && token.RevokedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(token => token.RevokedAt, clock.GetUtcNow()), ct);
}
