using System.Collections.Concurrent;

namespace Messenger.Api.Realtime;

public sealed record MessageConnection(Guid UserId, string TokenHash, Action Abort);

/// <summary>Сервер назначает адресата по сессии, клиент не может выбрать группу или чужой ID.</summary>
public sealed class MessageConnections
{
    private readonly ConcurrentDictionary<string, MessageConnection> _connections = new();
    public void Add(string id, MessageConnection connection) => _connections[id] = connection;
    public void Remove(string id) => _connections.TryRemove(id, out _);
    public MessageConnection? Find(string id) => _connections.GetValueOrDefault(id);
    public KeyValuePair<string, MessageConnection>[] ForUsers(IReadOnlyList<Guid> ids) =>
        _connections.Where(x => ids.Contains(x.Value.UserId)).ToArray();
}
