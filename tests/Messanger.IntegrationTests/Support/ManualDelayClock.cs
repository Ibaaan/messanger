using System.Threading.Channels;

namespace Messanger.IntegrationTests.Support;

internal sealed class ManualDelayClock : TimeProvider
{
    private readonly Channel<ManualDelayTimer> timers = Channel.CreateUnbounded<ManualDelayTimer>();
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualDelayTimer(callback, state, dueTime, period);
        timers.Writer.TryWrite(timer);
        return timer;
    }
    public Task<ManualDelayTimer> NextTimerAsync() => timers.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
}
