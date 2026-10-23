namespace Messanger.Authentication;

public sealed class RefreshTokenCleanupOptions
{
    public bool Enabled { get; set; } = true;
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);
    public int BatchSize { get; set; } = 1000;
    public int MaximumBatchesPerRun { get; set; } = 10;
}
