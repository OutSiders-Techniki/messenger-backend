using Messenger.Api.Hubs;
using Messenger.Application.DTOs;
using Messenger.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace Messenger.Api.Realtime;

public sealed class SignalRMessageEvents(
    IHubContext<MessagesHub> hub, MessageConnections connections, ISessionStore sessions,
    ILogger<SignalRMessageEvents> logger) : IMessageEvents
{
    public Task ReceivedAsync(IReadOnlyList<Guid> userIds, MessageReceivedEvent message, CancellationToken cancellationToken = default) =>
        SendAsync(userIds, "MessageReceived", message, cancellationToken);

    public Task DeletedAsync(IReadOnlyList<Guid> userIds, MessageDeletedEvent message, CancellationToken cancellationToken = default) =>
        SendAsync(userIds, "MessageDeleted", message, cancellationToken);

    private async Task SendAsync(IReadOnlyList<Guid> userIds, string eventName, object message, CancellationToken cancellationToken)
    {
        // Сессии проверяются непосредственно перед КАЖДОЙ доставкой. Одной проверки при подключении недостаточно.
        foreach (var (connectionId, connection) in connections.ForUsers(userIds))
        {
            try
            {
                var session = await sessions.GetByTokenHashAsync(connection.TokenHash, cancellationToken);
                if (session is null || session.UserId != connection.UserId || session.ExpiresAt <= DateTime.UtcNow)
                {
                    connections.Remove(connectionId);
                    connection.Abort();
                    continue;
                }
                await hub.Clients.Client(connectionId).SendAsync(eventName, message, cancellationToken);
            }
            catch (Exception ex)
            {
                // БД уже зафиксирована; пропущенное событие восстанавливается по REST после reconnect.
                // Закрываем соединение, чтобы клиент действительно выполнил reconnect и сверку.
                connections.Remove(connectionId);
                connection.Abort();
                logger.LogWarning("Realtime delivery deferred ({ErrorType})", ex.GetType().Name);
            }
        }
    }
}
