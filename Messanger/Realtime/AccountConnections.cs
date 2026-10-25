using Messanger.Security;
using Messanger.Operations;
using Microsoft.Extensions.Options;

namespace Messanger.Realtime;

// Single-instance connection closure, including connections which have not joined any chat.
internal sealed class AccountConnections(IOptions<SecurityLimitsOptions> settings, OperationalMetrics metrics)
{
    private readonly object gate = new();
    private readonly Dictionary<string, (long UserId, int SessionVersion, Guid FamilyId, Action Abort)> connections = new();

    public bool TryConnect(string connectionId, long userId, int sessionVersion, Guid familyId, Action abort)
    {
        lock (gate)
        {
            if (connections.ContainsKey(connectionId)) return true;
            var limits = settings.Value;
            if (limits.Enabled && (connections.Count >= limits.MaximumConnections ||
                connections.Values.Count(connection => connection.UserId == userId) >= limits.MaximumConnectionsPerUser)) return false;
            connections[connectionId] = (userId, sessionVersion, familyId, abort);
            metrics.ConnectionsChanged(1);
            return true;
        }
    }

    public void Disconnect(string connectionId)
    {
        lock (gate) if (connections.Remove(connectionId)) metrics.ConnectionsChanged(-1);
    }

    public void RevokeFamily(Guid familyId)
    {
        Action[] aborts;
        lock (gate)
        {
            var affected = connections.Where(item => item.Value.FamilyId == familyId).ToArray();
            aborts = affected.Select(item => item.Value.Abort).ToArray();
            foreach (var item in affected) connections.Remove(item.Key);
            metrics.ConnectionsChanged(-affected.Length);
        }
        foreach (var abort in aborts) abort();
    }

    public void Revoke(long userId) => RevokeBefore(userId, int.MaxValue);

    public void RevokeBefore(long userId, int sessionVersion)
    {
        Action[] aborts;
        lock (gate)
        {
            var affected = connections.Where(item => item.Value.UserId == userId && item.Value.SessionVersion < sessionVersion).ToArray();
            aborts = affected.Select(item => item.Value.Abort).ToArray();
            foreach (var item in affected) connections.Remove(item.Key);
            metrics.ConnectionsChanged(-affected.Length);
        }
        foreach (var abort in aborts) abort();
    }
}
