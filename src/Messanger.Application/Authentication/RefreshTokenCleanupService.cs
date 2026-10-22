using Messanger.Application.Persistence;

namespace Messanger.Application.Authentication;

public sealed class RefreshTokenCleanupService(IRefreshTokenCleanupRepository tokens, TimeProvider clock)
{
    public const int RetentionDays = 90;

    public async Task<RefreshTokenCleanupResult> RunAsync(int batchSize, int maximumBatches, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(batchSize, 10000);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBatches, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumBatches, 100);
        // Capture once so every batch in this pass applies the same retention boundary.
        var cutoff = clock.GetUtcNow().AddDays(-RetentionDays);
        var deleted = 0;
        for (var batch = 0; batch < maximumBatches; batch++)
        {
            ct.ThrowIfCancellationRequested();
            var count = await tokens.DeleteBatchAsync(cutoff, batchSize, ct);
            deleted += count;
            if (count < batchSize) return new(cutoff, deleted, false);
        }
        return new(cutoff, deleted, true);
    }
}
