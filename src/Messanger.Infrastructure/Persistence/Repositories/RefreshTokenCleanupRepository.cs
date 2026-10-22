using Messanger.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Messanger.Infrastructure.Persistence.Repositories;

internal sealed class RefreshTokenCleanupRepository(MessangerDbContext db) : IRefreshTokenCleanupRepository
{
    public Task<int> DeleteBatchAsync(DateTimeOffset expiresOnOrBefore, int batchSize, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(batchSize, 10000);
        // One PostgreSQL statement: bounded row locks, no entity loading, and no waiting on another cleanup worker.
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH candidates AS (
                SELECT token.id
                FROM refresh_token_families AS token
                WHERE token.expires_at <= {expiresOnOrBefore}
                  AND NOT EXISTS (
                      SELECT 1 FROM refresh_token_families AS newer
                      WHERE newer.family_id = token.family_id AND newer.expires_at > {expiresOnOrBefore}
                  )
                ORDER BY token.expires_at, token.id
                LIMIT {batchSize}
                FOR UPDATE OF token SKIP LOCKED
            )
            DELETE FROM refresh_token_families AS token
            USING candidates
            WHERE token.id = candidates.id
            """, ct);
    }
}
