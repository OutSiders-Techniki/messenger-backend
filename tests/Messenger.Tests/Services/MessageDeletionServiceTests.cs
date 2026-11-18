using Messenger.Application.DTOs;
using Messenger.Application.Exceptions;
using Messenger.Application.Interfaces;
using Messenger.Application.Services;

namespace Messenger.Tests.Services;

public sealed class MessageDeletionServiceTests
{
    [Fact]
    public async Task ClearSelf_ForwardsMissingSelectionAsAllOwnCopies()
    {
        var repository = new StubRepository();
        var events = new StubEvents();
        var service = new MessageDeletionService(repository, events);
        var userId = Guid.NewGuid();
        var dialogId = Guid.NewGuid();

        await service.DeleteSelfAsync(userId, dialogId, new());

        Assert.Equal(1, repository.Calls);
        Assert.Null(repository.Ids);
        Assert.False(repository.ForEveryone);
        Assert.Equal(userId, repository.UserId);
        Assert.Equal(dialogId, repository.DialogId);
        Assert.Empty(events.Deleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedDeletion_DeduplicatesIdsBeforeAtomicRepositoryCall(bool both)
    {
        var repository = new StubRepository();
        var service = new MessageDeletionService(repository, new StubEvents());
        var id = Guid.NewGuid();
        var ids = new[] { id, id };
        if (both)
            await service.DeleteBothAsync(Guid.NewGuid(), Guid.NewGuid(), new(ids));
        else
            await service.DeleteSelfAsync(Guid.NewGuid(), Guid.NewGuid(), new(ids));

        Assert.Equal(1, repository.Calls);
        Assert.Equal(new[] { id }, repository.Ids);
        Assert.Equal(both, repository.ForEveryone);
    }

    [Fact]
    public async Task EmptySelfSelection_IsNotInterpretedAsClearAll()
    {
        var repository = new StubRepository();
        var service = new MessageDeletionService(repository, new StubEvents());
        await service.DeleteSelfAsync(Guid.NewGuid(), Guid.NewGuid(), new([]));
        Assert.NotNull(repository.Ids);
        Assert.Empty(repository.Ids);
        Assert.Equal(1, repository.Calls);
    }

    [Fact]
    public async Task DeleteBoth_RequiresExplicitNonemptySelection()
    {
        var repository = new StubRepository();
        var service = new MessageDeletionService(repository, new StubEvents());
        await Assert.ThrowsAsync<ValidationException>(() => service.DeleteBothAsync(Guid.NewGuid(), Guid.NewGuid(), new(null)));
        await Assert.ThrowsAsync<ValidationException>(() => service.DeleteBothAsync(Guid.NewGuid(), Guid.NewGuid(), new([])));
        Assert.Equal(0, repository.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidSelection_IsRejectedBeforeStorage(bool both)
    {
        var repository = new StubRepository();
        var events = new StubEvents();
        var service = new MessageDeletionService(repository, events);
        foreach (var ids in new[] { new[] { Guid.Empty }, Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToArray() })
        {
            if (both)
                await Assert.ThrowsAsync<ValidationException>(() => service.DeleteBothAsync(Guid.NewGuid(), Guid.NewGuid(), new(ids)));
            else
                await Assert.ThrowsAsync<ValidationException>(() => service.DeleteSelfAsync(Guid.NewGuid(), Guid.NewGuid(), new(ids)));
        }
        Assert.Equal(0, repository.Calls);
        Assert.Empty(events.Deleted);
    }

    [Fact]
    public async Task Notifications_UseCommittedIdsRecipientsAndOrderedRevisions()
    {
        var userId = Guid.NewGuid();
        var peerId = Guid.NewGuid();
        var dialogId = Guid.NewGuid();
        var first = new MessageDeletedEvent(dialogId, Guid.NewGuid(), true, 8);
        var second = new MessageDeletedEvent(dialogId, Guid.NewGuid(), true, 9);
        var repository = new StubRepository { Result = new([second, first], [userId, peerId]) };
        var events = new StubEvents();
        var service = new MessageDeletionService(repository, events);

        // Неизвестный ID в запросе не создаёт лишнего события.
        await service.DeleteBothAsync(userId, dialogId, new([first.Id, second.Id, Guid.NewGuid()]));

        Assert.Equal(new[] { first, second }, events.Deleted.Select(x => x.Event));
        Assert.All(events.Deleted, x => Assert.Equal(new[] { userId, peerId }, x.Recipients));
    }

    [Fact]
    public async Task RejectedRepositoryOperation_DoesNotNotify()
    {
        var repository = new StubRepository { Failure = new DialogNotFoundException() };
        var events = new StubEvents();
        var service = new MessageDeletionService(repository, events);
        await Assert.ThrowsAsync<DialogNotFoundException>(() => service.DeleteSelfAsync(Guid.NewGuid(), Guid.NewGuid(), new()));
        Assert.Empty(events.Deleted);
    }

    private sealed class StubRepository : IMessageBatchRepository
    {
        public MessageBatchDeleteResult Result { get; init; } = new([], []);
        public Exception? Failure { get; init; }
        public int Calls { get; private set; }
        public Guid[]? Ids { get; private set; }
        public bool ForEveryone { get; private set; }
        public Guid UserId { get; private set; }
        public Guid DialogId { get; private set; }
        public Task<MessageBatchDeleteResult> DeleteBatchAsync(Guid userId, Guid dialogId, Guid[]? messageIds, bool forEveryone, CancellationToken cancellationToken = default)
        {
            Calls++; UserId = userId; DialogId = dialogId; Ids = messageIds; ForEveryone = forEveryone;
            return Failure is null ? Task.FromResult(Result) : Task.FromException<MessageBatchDeleteResult>(Failure);
        }
    }

    private sealed class StubEvents : IMessageEvents
    {
        public List<(IReadOnlyList<Guid> Recipients, MessageDeletedEvent Event)> Deleted { get; } = [];
        public Task DeletedAsync(IReadOnlyList<Guid> userIds, MessageDeletedEvent message, CancellationToken cancellationToken = default)
        { Deleted.Add((userIds, message)); return Task.CompletedTask; }
        public Task ReceivedAsync(IReadOnlyList<Guid> userIds, MessageReceivedEvent message, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
