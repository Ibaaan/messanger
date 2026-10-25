namespace Messanger.Operations;

public sealed record MetricsSnapshot(IReadOnlyList<RequestMetric> Requests, IReadOnlyDictionary<string, long> SecurityEvents,
    int ActiveHubConnections, long DatabaseCommands, double DatabaseCommandMilliseconds, long IdleDatabaseConnections, long UsedDatabaseConnections);
