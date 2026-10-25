namespace Messanger.Operations;

public sealed record RequestMetric(string Operation, string Method, int StatusClass, long Count, double TotalMilliseconds, double MaximumMilliseconds);
