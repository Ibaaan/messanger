using Messanger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Messanger.Operations;

public sealed class ReadinessMonitor(IServiceScopeFactory scopes, IOptions<OperationalOptions> settings, TimeProvider clock,
    ILogger<ReadinessMonitor> logger) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private long checkedAt;
    private bool ready;
    public async Task<bool> CheckAsync(CancellationToken ct)
    {
        var options = settings.Value;
        if (clock.GetUtcNow().UtcTicks - Interlocked.Read(ref checkedAt) < options.ReadinessCache.Ticks) return Volatile.Read(ref ready);
        if (!await gate.WaitAsync(0, ct)) return false;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(options.ReadinessTimeout);
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MessangerDbContext>();
            var pending = await db.Database.GetPendingMigrationsAsync(timeout.Token);
            ready = !pending.Any() && !db.Database.HasPendingModelChanges();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            ready = false;
            logger.LogWarning("Database readiness probe failed ({FailureType})", exception.GetType().Name);
        }
        finally { Interlocked.Exchange(ref checkedAt, clock.GetUtcNow().UtcTicks); gate.Release(); }
        return ready;
    }
    public void Dispose() => gate.Dispose();
}
