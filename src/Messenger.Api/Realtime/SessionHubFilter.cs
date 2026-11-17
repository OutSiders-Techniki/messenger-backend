using Messenger.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace Messenger.Api.Realtime;

public sealed class SessionHubFilter(MessageConnections connections, ISessionStore sessions) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var connection = connections.Find(invocationContext.Context.ConnectionId);
        var session = connection is null ? null : await sessions.GetByTokenHashAsync(connection.TokenHash, invocationContext.Context.ConnectionAborted);
        if (session is null || session.UserId != connection!.UserId || session.ExpiresAt <= DateTime.UtcNow)
        {
            invocationContext.Context.Abort();
            throw new HubException("Сессия отсутствует или закончилась. Войдите заново.");
        }
        return await next(invocationContext);
    }
}
