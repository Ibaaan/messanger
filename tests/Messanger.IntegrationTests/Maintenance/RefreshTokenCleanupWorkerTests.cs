using System.Threading.Channels;
using Messanger.Application.Authentication;
using Messanger.Application.Persistence;
using Messanger.Authentication;
using Messanger.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Messanger.IntegrationTests.Maintenance;

internal static class RefreshTokenCleanupWorkerTests
{
    public static async Task FailureRetriesOnScheduleWithANewDisposedScope()
    {
        var clock = new ManualDelayClock();
        var calls = Channel.CreateUnbounded<int>();
        var scopes = 0;
        var disposals = 0;
        var services = new ServiceCollection().AddSingleton<TimeProvider>(clock).AddScoped<RefreshTokenCleanupService>();
        services.AddScoped<IRefreshTokenCleanupRepository>(_ =>
        {
            var id = Interlocked.Increment(ref scopes);
            return new CleanupRepositoryStub((_, _, _) =>
            {
                calls.Writer.TryWrite(id);
                return id == 1 ? Task.FromException<int>(new InvalidOperationException("Temporary database outage")) : Task.FromResult(0);
            }, () => Interlocked.Increment(ref disposals));
        });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var options = new RefreshTokenCleanupOptions { Interval = TimeSpan.FromMinutes(3) };
        using var worker = Worker(provider, clock, options);
        await worker.StartAsync(default);
        var firstDelay = await clock.NextTimerAsync();
        TestAssert.Equal(1, disposals, "A failed pass must dispose its scoped dependencies.");
        TestAssert.Equal(options.Interval, firstDelay.DueTime, "Wait the configured interval after failure instead of spinning.");
        firstDelay.Fire();
        await clock.NextTimerAsync();
        TestAssert.Equal(1, await calls.Reader.ReadAsync(), "The worker starts with a pass immediately.");
        TestAssert.Equal(2, await calls.Reader.ReadAsync(), "A later pass uses a fresh scope after the outage.");
        TestAssert.Equal(2, disposals, "Successful passes dispose their scope too.");
        await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(10));
    }

    public static async Task ShutdownCancelsDatabaseWorkAndDisposesItsScope()
    {
        var clock = new ManualDelayClock();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = false;
        var services = new ServiceCollection().AddSingleton<TimeProvider>(clock).AddScoped<RefreshTokenCleanupService>();
        services.AddScoped<IRefreshTokenCleanupRepository>(_ => new CleanupRepositoryStub(async (_, _, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return 0;
        }, () => disposed = true));
        await using var provider = services.BuildServiceProvider();
        using var worker = Worker(provider, clock, new());
        await worker.StartAsync(default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(10));
        TestAssert.True(disposed, "Shutdown must propagate cancellation and release the active scope.");
    }

    public static async Task EachPassHonorsItsBatchBudgetBeforeWaiting()
    {
        var clock = new ManualDelayClock();
        var calls = 0;
        var services = new ServiceCollection().AddSingleton<TimeProvider>(clock).AddScoped<RefreshTokenCleanupService>();
        services.AddScoped<IRefreshTokenCleanupRepository>(_ => new CleanupRepositoryStub((_, batchSize, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(batchSize);
        }, () => { }));
        await using var provider = services.BuildServiceProvider();
        using var worker = Worker(provider, clock, new() { BatchSize = 3, MaximumBatchesPerRun = 2 });
        await worker.StartAsync(default);
        await clock.NextTimerAsync();
        TestAssert.Equal(2, calls, "Even a permanent backlog cannot keep a pass running beyond its budget.");
        await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static RefreshTokenCleanupWorker Worker(IServiceProvider provider, TimeProvider clock, RefreshTokenCleanupOptions options) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(options), clock, NullLogger<RefreshTokenCleanupWorker>.Instance);
}
