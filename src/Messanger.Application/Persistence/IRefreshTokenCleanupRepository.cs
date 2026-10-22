namespace Messanger.Application.Persistence;

public interface IRefreshTokenCleanupRepository
{
    // Atomic, directly committed batch. Preserve families with any record newer than this cutoff.
    Task<int> DeleteBatchAsync(DateTimeOffset expiresOnOrBefore, int batchSize, CancellationToken ct);
}
