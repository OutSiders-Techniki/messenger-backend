using Messenger.Api.Hubs;
using Messenger.Api.Realtime;
using Messenger.Application.DTOs;
using Messenger.Application.Interfaces;
using Messenger.Domain.Entities;
using Microsoft.AspNetCore.SignalR;
using Npgsql;

namespace Messenger.Tests.Services;

public sealed class RealtimeDeliveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedSessionRevalidation_ClosesOnlyAffectedConnectionForResynchronization(bool deletion)
    {
        var recipient = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var aborted = false;
        var strangerAborted = false;
        var connections = new MessageConnections();
        connections.Add("recipient", new MessageConnection(recipient, "recipient-token-hash", () => aborted = true));
        connections.Add("stranger", new MessageConnection(stranger, "stranger-token-hash", () => strangerAborted = true));
        var logger = new RecordingLogger<SignalRMessageEvents>();
        var hub = new UnusedHubContext();
        var sessions = new FailingSessions();
        var events = new SignalRMessageEvents(hub, connections, sessions, logger);

        if (deletion)
            await events.DeletedAsync([recipient], new MessageDeletedEvent(Guid.NewGuid(), Guid.NewGuid(), true, 2));
        else
            await events.ReceivedAsync([recipient], new MessageReceivedEvent(
                new MessageResponse(Guid.NewGuid(), "TESTUSER", 1, Guid.NewGuid(), DateTimeOffset.UtcNow, new byte[12], new byte[16]), 1));

        Assert.True(aborted);
        Assert.Null(connections.Find("recipient"));
        Assert.False(strangerAborted);
        Assert.NotNull(connections.Find("stranger"));
        Assert.Equal(1, sessions.LookupCalls);
        Assert.Equal(0, hub.DeliveryAttempts);
        Assert.Contains(logger.Warnings, x => x.Contains("NpgsqlException"));
        Assert.DoesNotContain(logger.Warnings, x => x.Contains("session-secret"));
    }

    private sealed class UnusedHubContext : IHubContext<MessagesHub>
    {
        public int DeliveryAttempts { get; private set; }
        public IHubClients Clients
        {
            get { DeliveryAttempts++; throw new InvalidOperationException("Session validation must fail before delivery."); }
        }
        public IGroupManager Groups => throw new NotSupportedException();
    }

    private sealed class FailingSessions : ISessionStore
    {
        public int LookupCalls { get; private set; }
        public Task<Session?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        { LookupCalls++; throw new NpgsqlException("session-secret"); }
        public Task<Session> CreateAsync(Guid userId, string tokenHash, DateTime expiresAt, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<bool> RevokeAsync(string tokenHash, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
