namespace Messanger.IntegrationTests.Support;

internal sealed class MutableClock(DateTimeOffset? initial = null) : TimeProvider
{
    private DateTimeOffset now = initial ?? DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan duration) => now += duration;
}
