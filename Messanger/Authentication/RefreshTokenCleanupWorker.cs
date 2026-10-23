using Messanger.Application.Authentication;
using Microsoft.Extensions.Options;

namespace Messanger.Authentication;

public sealed class RefreshTokenCleanupWorker(IServiceScopeFactory scopes, IOptions<RefreshTokenCleanupOptions> settings,
    TimeProvider clock, ILogger<RefreshTokenCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = settings.Value;
        if (!options.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<RefreshTokenCleanupService>()
                    .RunAsync(options.BatchSize, options.MaximumBatchesPerRun, stoppingToken);
                logger.LogInformation("Refresh-token cleanup deleted {DeletedRows} rows through {Cutoff}; batch limit reached: {BatchLimitReached}",
                    result.DeletedRows, result.Cutoff, result.BatchLimitReached);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Refresh-token cleanup failed; retrying on the next scheduled interval");
            }
            try { await Task.Delay(options.Interval, clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
