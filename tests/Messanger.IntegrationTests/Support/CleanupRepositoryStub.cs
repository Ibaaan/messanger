using Messanger.Application.Persistence;

namespace Messanger.IntegrationTests.Support;

internal sealed class CleanupRepositoryStub(Func<DateTimeOffset, int, CancellationToken, Task<int>> delete,
    Action disposed) : IRefreshTokenCleanupRepository, IDisposable
{
    public Task<int> DeleteBatchAsync(DateTimeOffset expiresOnOrBefore, int batchSize, CancellationToken ct) =>
        delete(expiresOnOrBefore, batchSize, ct);
    public void Dispose() => disposed();
}
