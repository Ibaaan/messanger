namespace Messanger.Operations;

public sealed class OperationalOptions
{
    public TimeSpan ReadinessTimeout { get; set; } = TimeSpan.FromSeconds(3);
    public TimeSpan ReadinessCache { get; set; } = TimeSpan.FromSeconds(5);
}
