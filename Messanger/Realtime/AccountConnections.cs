namespace Messanger.Realtime;

// Single-instance connection closure, including connections which have not joined any chat.
internal sealed class AccountConnections
{
    private readonly object gate = new();
    private readonly Dictionary<string, (long UserId, int SessionVersion, Guid FamilyId, Action Abort)> connections = new();

    public void Connect(string connectionId, long userId, int sessionVersion, Guid familyId, Action abort)
    {
        lock (gate) connections[connectionId] = (userId, sessionVersion, familyId, abort);
    }

    public void Disconnect(string connectionId)
    {
        lock (gate) connections.Remove(connectionId);
    }

    public void RevokeFamily(Guid familyId)
    {
        Action[] aborts;
        lock (gate)
        {
            var affected = connections.Where(item => item.Value.FamilyId == familyId).ToArray();
            aborts = affected.Select(item => item.Value.Abort).ToArray();
            foreach (var item in affected) connections.Remove(item.Key);
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
        }
        foreach (var abort in aborts) abort();
    }
}
