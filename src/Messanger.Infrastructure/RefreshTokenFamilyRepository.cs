using Messanger.Application;
using Messanger.Domain;
using Microsoft.EntityFrameworkCore;

namespace Messanger.Infrastructure;

internal sealed class RefreshTokenFamilyRepository(MessangerDbContext db) : IRefreshTokenFamilyRepository
{
    public Task<RefreshTokenFamily?> GetByHashAsync(string jtiHash, CancellationToken cancellationToken = default)
    {
        return db.RefreshTokens.SingleOrDefaultAsync(x => x.JtiHash == jtiHash, cancellationToken);
    }

    public void Add(RefreshTokenFamily token)
    {
        db.RefreshTokens.Add(token);
    }

    public async Task RevokeFamilyAsync(Guid familyId, DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default)
    {
        var tokens = await db.RefreshTokens.Where(x => x.FamilyId == familyId && x.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in tokens) token.RevokedAt = revokedAt;
    }

    public async Task RevokeForUserAsync(long userId, DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default)
    {
        var tokens = await db.RefreshTokens.Where(x => x.UserId == userId && x.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in tokens) token.RevokedAt = revokedAt;
    }
}