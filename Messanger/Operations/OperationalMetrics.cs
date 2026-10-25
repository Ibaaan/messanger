using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Messanger.Operations;

public sealed class OperationalMetrics : IDisposable
{
    private readonly ConcurrentDictionary<(string Operation, string Method, int Status), RequestMetric> requests = new();
    private readonly ConcurrentDictionary<string, long> security = new();
    private readonly Meter meter = new("Messanger", "1.0");
    private readonly Counter<long> requestCounter;
    private readonly Histogram<double> requestDuration;
    private readonly Counter<long> securityCounter;
    private readonly MeterListener database = new();
    private readonly object poolGate = new();
    private int hubs;
    private long commands;
    private double commandMilliseconds;
    private long idle;
    private long used;

    public OperationalMetrics()
    {
        requestCounter = meter.CreateCounter<long>("messanger.http.requests");
        requestDuration = meter.CreateHistogram<double>("messanger.http.duration", "ms");
        securityCounter = meter.CreateCounter<long>("messanger.security.events");
        meter.CreateObservableGauge("messanger.hub.connections", () => Volatile.Read(ref hubs));
        database.InstrumentPublished = (instrument, listener) => { if (instrument.Meter.Name == "Npgsql") listener.EnableMeasurementEvents(instrument); };
        database.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            if (instrument.Name == "db.client.operation.duration" && IsAppPool(tags))
            {
                Interlocked.Increment(ref commands);
                lock (poolGate) commandMilliseconds += value * 1000;
            }
        });
        database.SetMeasurementEventCallback<int>((instrument, value, tags, _) => Pool(instrument.Name, value, tags));
        database.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Pool(instrument.Name, value, tags));
        database.Start();
    }

    public void Request(string operation, string method, int statusCode, double milliseconds)
    {
        method = method is "GET" or "POST" or "PATCH" or "DELETE" or "OPTIONS" or "HEAD" ? method : "OTHER";
        var status = statusCode / 100;
        requests.AddOrUpdate((operation, method, status), new RequestMetric(operation, method, status, 1, milliseconds, milliseconds),
            (_, current) => current with { Count = current.Count + 1, TotalMilliseconds = current.TotalMilliseconds + milliseconds,
                MaximumMilliseconds = Math.Max(current.MaximumMilliseconds, milliseconds) });
        requestCounter.Add(1, new("operation", operation), new("method", method), new("status_class", status));
        requestDuration.Record(milliseconds, new("operation", operation), new("method", method));
    }
    public void Security(string action) { security.AddOrUpdate(action, 1, (_, count) => count + 1); securityCounter.Add(1, new KeyValuePair<string, object?>("action", action)); }
    public void ConnectionsChanged(int delta) => Interlocked.Add(ref hubs, delta);
    public MetricsSnapshot Snapshot()
    {
        lock (poolGate)
        {
            idle = used = 0;
            database.RecordObservableInstruments();
            return new(requests.Values.OrderBy(value => value.Operation).ThenBy(value => value.Method).ToArray(),
                new Dictionary<string, long>(security), Volatile.Read(ref hubs), Interlocked.Read(ref commands), commandMilliseconds, idle, used);
        }
    }
    private void Pool(string name, long count, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        if (name != "db.client.connection.count" || !IsAppPool(tags)) return;
        foreach (var tag in tags)
            if (tag.Key == "state")
            {
                lock (poolGate) { if (tag.Value?.ToString() == "idle") idle += count; else if (tag.Value?.ToString() == "used") used += count; }
            }
    }
    private static bool IsAppPool(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        foreach (var tag in tags) if (tag.Key == "db.client.connection.pool.name" && tag.Value?.ToString() == "messanger") return true;
        return false;
    }
    public void Dispose() { database.Dispose(); meter.Dispose(); }
}
