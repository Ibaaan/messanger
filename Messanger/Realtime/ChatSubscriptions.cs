namespace Messanger.Realtime;

internal sealed class ChatSubscriptions
{
    private readonly object gate = new();
    private readonly Dictionary<string, (long UserId, HashSet<long> Chats)> connections = new();
    private readonly Dictionary<(long ChatId, long UserId), long> membershipVersions = new();

    public void Connect(string connectionId, long userId)
    {
        lock (gate) connections[connectionId] = (userId, []);
    }

    public void Disconnect(string connectionId)
    {
        lock (gate) connections.Remove(connectionId);
    }

    public long Version(long chatId, long userId)
    {
        lock (gate) return membershipVersions.GetValueOrDefault((chatId, userId));
    }

    public bool Join(string connectionId, long chatId, long expectedVersion)
    {
        lock (gate)
        {
            if (!connections.TryGetValue(connectionId, out var connection)
                || membershipVersions.GetValueOrDefault((chatId, connection.UserId)) != expectedVersion) return false;
            connection.Chats.Add(chatId);
            return true;
        }
    }

    public void Leave(string connectionId, long chatId)
    {
        lock (gate)
            if (connections.TryGetValue(connectionId, out var connection)) connection.Chats.Remove(chatId);
    }

    public string[] Revoke(long chatId, long userId)
    {
        lock (gate)
        {
            var key = (chatId, userId);
            membershipVersions[key] = membershipVersions.GetValueOrDefault(key) + 1;
            var affected = new List<string>();
            foreach (var (id, connection) in connections)
                if (connection.UserId == userId && connection.Chats.Remove(chatId)) affected.Add(id);
            return affected.ToArray();
        }
    }

    public string[] ActiveConnections(long chatId, IReadOnlyList<long> activeUserIds)
    {
        var allowed = activeUserIds.ToHashSet();
        lock (gate)
        {
            var result = new List<string>();
            foreach (var (id, connection) in connections)
            {
                if (!connection.Chats.Contains(chatId)) continue;
                if (allowed.Contains(connection.UserId)) result.Add(id);
                else connection.Chats.Remove(chatId);
            }
            return result.ToArray();
        }
    }
}
