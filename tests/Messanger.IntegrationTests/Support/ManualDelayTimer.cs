namespace Messanger.IntegrationTests.Support;

internal sealed class ManualDelayTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) : ITimer
{
    private bool disposed;
    public TimeSpan DueTime { get; private set; } = dueTime;
    public TimeSpan Period { get; private set; } = period;
    public void Fire() { if (!disposed) callback(state); }
    public bool Change(TimeSpan dueTime, TimeSpan period)
    {
        DueTime = dueTime;
        Period = period;
        return !disposed;
    }
    public void Dispose() => disposed = true;
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}
