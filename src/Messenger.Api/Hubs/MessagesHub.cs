using System.Security.Claims;
using Messenger.Api.Realtime;
using Messenger.Application.Interfaces;
using Messenger.Application.Options;
using Messenger.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Messenger.Api.Hubs;

[Authorize]
public sealed class MessagesHub(MessageConnections connections, ISessionStore sessions, IOptions<SessionCookieOptions> options) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        if (!Guid.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || http is null || !http.Request.Cookies.TryGetValue(options.Value.CookieName, out var token))
        {
            Context.Abort();
            return;
        }
        var tokenHash = SessionTokenGenerator.Hash(token);
        var session = await sessions.GetByTokenHashAsync(tokenHash, Context.ConnectionAborted);
        if (session is null || session.UserId != userId || session.ExpiresAt <= DateTime.UtcNow)
        {
            Context.Abort();
            return;
        }
        connections.Add(Context.ConnectionId, new MessageConnection(userId, tokenHash, Context.Abort));
        await base.OnConnectedAsync();
    }

    // Сам факт успешного вызова означает, что фильтр снова проверил сессию.
    public Task Ping() => Task.CompletedTask;

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Remove(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
