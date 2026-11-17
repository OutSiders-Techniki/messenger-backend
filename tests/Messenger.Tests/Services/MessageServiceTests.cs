using Messenger.Application.DTOs;
using Messenger.Application.Exceptions;
using Messenger.Application.Interfaces;
using Messenger.Application.Services;

namespace Messenger.Tests.Services;

public sealed class MessageServiceTests
{
    private static SendMessageRequest Packet() => new(1, Guid.NewGuid(), DateTimeOffset.Parse("2026-11-17T12:00:00.000Z"), new byte[12], new byte[16]);

    public static IEnumerable<object[]> InvalidPackets()
    {
        var valid = Packet();
        yield return [valid with { Version = 2 }];
        yield return [valid with { Id = Guid.Empty }];
        yield return [valid with { SentAt = default }];
        yield return [valid with { SentAt = valid.SentAt.ToOffset(TimeSpan.FromHours(1)) }];
        yield return [valid with { SentAt = valid.SentAt.AddTicks(1) }];
        yield return [valid with { Iv = null }];
        yield return [valid with { Iv = new byte[11] }];
        yield return [valid with { Iv = new byte[13] }];
        yield return [valid with { Ciphertext = null }];
        yield return [valid with { Ciphertext = new byte[15] }];
        yield return [valid with { Ciphertext = new byte[MessageLimits.MaxCiphertextBytes + 1] }];
    }

    [Theory]
    [MemberData(nameof(InvalidPackets))]
    public async Task InvalidPacket_IsRejectedBeforeStorageOrDelivery(SendMessageRequest packet)
    {
        var repository = new StubRepository();
        var events = new StubEvents();
        var service = new MessageService(repository, events);
        await Assert.ThrowsAsync<ValidationException>(() => service.SendAsync(Guid.NewGuid(), Guid.NewGuid(), packet));
        Assert.Equal(0, repository.SendCalls);
        Assert.Empty(events.Received);
    }

    [Fact]
    public async Task FreshPacket_NotifiesOnlyRepositoryRecipients()
    {
        var packet = Packet();
        var userId = Guid.NewGuid();
        var peerId = Guid.NewGuid();
        var dialogId = Guid.NewGuid();
        var response = new MessageResponse(dialogId, userId, packet.Version, packet.Id, packet.SentAt, packet.Iv!, packet.Ciphertext!);
        var repository = new StubRepository { SendResult = new(new(response, true, false, 7), [userId, peerId]) };
        var events = new StubEvents();
        var service = new MessageService(repository, events);

        var result = await service.SendAsync(userId, dialogId, packet);

        Assert.True(result.Created);
        Assert.Equal(1, repository.SendCalls);
        var sent = Assert.Single(events.Received);
        Assert.Equal(new[] { userId, peerId }, sent.Users);
        Assert.Equal(packet.Id, sent.Event.Message.Id);
        Assert.Equal(7, sent.Event.Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replay_DoesNotNotifyOrRecreateCopies(bool deleted)
    {
        var repository = new StubRepository { SendResult = new(new(null, false, deleted, 9), []) };
        var events = new StubEvents();
        var service = new MessageService(repository, events);
        var result = await service.SendAsync(Guid.NewGuid(), Guid.NewGuid(), Packet());
        Assert.False(result.Created);
        Assert.Equal(deleted, result.Deleted);
        Assert.Empty(events.Received);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task InvalidPageSize_IsRejected(int limit)
    {
        var service = new MessageService(new StubRepository(), new StubEvents());
        await Assert.ThrowsAsync<ValidationException>(() => service.GetHistoryAsync(Guid.NewGuid(), Guid.NewGuid(), limit));
    }

    [Fact]
    public async Task Sync_BoundsAndDeduplicatesCachedIdentifiers()
    {
        var repository = new StubRepository();
        var service = new MessageService(repository, new StubEvents());
        await Assert.ThrowsAsync<ValidationException>(() => service.SyncAsync(Guid.NewGuid(), Guid.NewGuid(), new(null)));
        await Assert.ThrowsAsync<ValidationException>(() => service.SyncAsync(Guid.NewGuid(), Guid.NewGuid(), new([Guid.Empty])));
        await Assert.ThrowsAsync<ValidationException>(() => service.SyncAsync(Guid.NewGuid(), Guid.NewGuid(), new(Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToArray())));
        var id = Guid.NewGuid();
        await service.SyncAsync(Guid.NewGuid(), Guid.NewGuid(), new([id, id]));
        Assert.Equal(new[] { id }, repository.SyncIds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delete_NotifiesOnlyAfterChangedResult(bool changed)
    {
        var userId = Guid.NewGuid();
        var dialogId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var repository = new StubRepository { DeleteResult = new(changed, 10, [userId]) };
        var events = new StubEvents();
        var service = new MessageService(repository, events);
        await service.DeleteAsync(userId, dialogId, messageId, false);
        Assert.Equal(changed ? 1 : 0, events.Deleted.Count);
        if (changed)
        {
            var sent = Assert.Single(events.Deleted);
            Assert.Equal(new[] { userId }, sent.Users);
            Assert.Equal(new MessageDeletedEvent(dialogId, messageId, false, 10), sent.Event);
        }
    }

    private sealed class StubEvents : IMessageEvents
    {
        public List<(IReadOnlyList<Guid> Users, MessageReceivedEvent Event)> Received { get; } = [];
        public List<(IReadOnlyList<Guid> Users, MessageDeletedEvent Event)> Deleted { get; } = [];
        public Task ReceivedAsync(IReadOnlyList<Guid> userIds, MessageReceivedEvent message, CancellationToken cancellationToken = default)
        { Received.Add((userIds, message)); return Task.CompletedTask; }
        public Task DeletedAsync(IReadOnlyList<Guid> userIds, MessageDeletedEvent message, CancellationToken cancellationToken = default)
        { Deleted.Add((userIds, message)); return Task.CompletedTask; }
    }

    private sealed class StubRepository : IMessageRepository
    {
        public int SendCalls { get; private set; }
        public Guid[]? SyncIds { get; private set; }
        public MessageSendResult SendResult { get; init; } = new(new(null, false, false, 0), []);
        public MessageDeleteResult DeleteResult { get; init; } = new(false, 0, []);
        public Task<MessageSendResult> SendAsync(Guid userId, Guid dialogId, SendMessageRequest request, CancellationToken cancellationToken = default)
        { SendCalls++; return Task.FromResult(SendResult); }
        public Task<MessageHistoryResponse> GetHistoryAsync(Guid userId, Guid dialogId, int limit, string? before, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MessageHistoryResponse([], null, 0));
        public Task<SyncMessagesResponse> SyncAsync(Guid userId, Guid dialogId, Guid[] ids, CancellationToken cancellationToken = default)
        { SyncIds = ids; return Task.FromResult(new SyncMessagesResponse([], 0)); }
        public Task<MessageDeleteResult> DeleteAsync(Guid userId, Guid dialogId, Guid messageId, bool forEveryone, CancellationToken cancellationToken = default) =>
            Task.FromResult(DeleteResult);
    }
}
