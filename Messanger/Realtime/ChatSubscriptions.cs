using System.Collections.Concurrent;

namespace Messanger.Realtime;

internal sealed class ChatSubscriptions
{
    private readonly ConcurrentDictionary<string, (long UserId, ConcurrentDictionary<long, byte> Chats)> connections = new();

    public void Connect(string connectionId, long userId) => connections[connectionId] = (userId, new());
    public void Disconnect(string connectionId) => connections.TryRemove(connectionId, out _);
    public void Join(string connectionId, long chatId)
    {
        if (connections.TryGetValue(connectionId, out var connection)) connection.Chats[chatId] = 0;
    }
    public void Leave(string connectionId, long chatId)
    {
        if (connections.TryGetValue(connectionId, out var connection)) connection.Chats.TryRemove(chatId, out _);
    }

    public string[] ActiveConnections(long chatId, IReadOnlyList<long> activeUserIds)
    {
        var allowed = activeUserIds.ToHashSet();
        var result = new List<string>();
        foreach (var (id, connection) in connections)
        {
            if (!connection.Chats.ContainsKey(chatId)) continue;
            if (allowed.Contains(connection.UserId)) result.Add(id);
            else connection.Chats.TryRemove(chatId, out _);
        }
        return result.ToArray();
    }
}
