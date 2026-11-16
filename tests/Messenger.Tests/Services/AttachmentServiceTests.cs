using Messenger.Application.DTOs;
using Messenger.Application.Exceptions;
using Messenger.Application.Interfaces;
using Messenger.Application.Services;

namespace Messenger.Tests.Services;

public class AttachmentServiceTests
{
    private readonly FakeAttachmentRepository _attachments = new();

    private AttachmentService CreateSut() => new(_attachments);

    [Fact]
    public async Task GetMetadataAsync_ForOwner_ReturnsMetadata()
    {
        var ownerId = Guid.NewGuid();
        var dialogId = Guid.NewGuid();
        var clientMessageId = Guid.NewGuid();
        var clientAttachmentId = Guid.NewGuid();

        _attachments.Add(ownerId, dialogId, new AttachmentMetadataResponse(
            clientMessageId,
            clientAttachmentId,
            1024,
            DateTime.UtcNow,
            [1, 2, 3]));

        var sut = CreateSut();

        var metadata = await sut.GetMetadataAsync(
            ownerId,
            dialogId,
            clientMessageId,
            clientAttachmentId);

        Assert.Equal(clientMessageId, metadata.ClientMessageId);
        Assert.Equal(clientAttachmentId, metadata.ClientAttachmentId);
        Assert.Equal(1024, metadata.Size);
        Assert.Equal(new byte[] { 1, 2, 3 }, metadata.EncryptedMetadata);
    }

    [Fact]
    public async Task GetMetadataAsync_ForStranger_ThrowsNotFound()
    {
        var ownerId = Guid.NewGuid();
        var dialogId = Guid.NewGuid();
        var clientMessageId = Guid.NewGuid();
        var clientAttachmentId = Guid.NewGuid();

        _attachments.Add(ownerId, dialogId, new AttachmentMetadataResponse(
            clientMessageId,
            clientAttachmentId,
            1024,
            DateTime.UtcNow,
            [1]));

        var sut = CreateSut();

        await Assert.ThrowsAsync<AttachmentNotFoundException>(() => sut.GetMetadataAsync(
            Guid.NewGuid(),
            dialogId,
            clientMessageId,
            clientAttachmentId));
    }

    [Fact]
    public async Task GetMetadataAsync_WithUnknownId_ThrowsNotFound()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<AttachmentNotFoundException>(() => sut.GetMetadataAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid()));
    }

    [Fact]
    public async Task ListAsync_ForOwner_ReturnsAttachmentsOfCopy()
    {
        var ownerId = Guid.NewGuid();
        var dialogId = Guid.NewGuid();
        var clientMessageId = Guid.NewGuid();

        _attachments.Add(ownerId, dialogId, new AttachmentMetadataResponse(
            clientMessageId,
            Guid.NewGuid(),
            1024,
            DateTime.UtcNow,
            [1]));

        _attachments.Add(Guid.NewGuid(), dialogId, new AttachmentMetadataResponse(
            clientMessageId,
            Guid.NewGuid(),
            1024,
            DateTime.UtcNow,
            [2]));

        var sut = CreateSut();

        var list = await sut.ListAsync(ownerId, dialogId, clientMessageId);

        var item = Assert.Single(list);
        Assert.Equal(clientMessageId, item.ClientMessageId);
    }

    [Fact]
    public async Task ListAsync_ForStranger_ReturnsEmpty()
    {
        var ownerId = Guid.NewGuid();
        var dialogId = Guid.NewGuid();
        var clientMessageId = Guid.NewGuid();

        _attachments.Add(ownerId, dialogId, new AttachmentMetadataResponse(
            clientMessageId,
            Guid.NewGuid(),
            1024,
            DateTime.UtcNow,
            [1]));

        var sut = CreateSut();

        var list = await sut.ListAsync(Guid.NewGuid(), dialogId, clientMessageId);

        Assert.Empty(list);
    }

    private sealed class FakeAttachmentRepository : IAttachmentRepository
    {
        private readonly List<(Guid OwnerId, Guid DialogId, AttachmentMetadataResponse Item)> _items = new();

        public Task<AttachmentMetadataResponse?> FindForOwnerAsync(
            Guid dialogId,
            Guid clientMessageId,
            Guid clientAttachmentId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            AttachmentMetadataResponse? result = null;

            foreach (var (ownerId, itemDialogId, item) in _items)
            {
                if (item.ClientMessageId == clientMessageId
                    && item.ClientAttachmentId == clientAttachmentId
                    && itemDialogId == dialogId
                    && ownerId == userId)
                {
                    result = item;
                }
            }

            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<AttachmentMetadataResponse>> ListForOwnerAsync(
            Guid dialogId,
            Guid clientMessageId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<AttachmentMetadataResponse> result = _items
                .Where(item =>
                    item.OwnerId == userId
                    && item.DialogId == dialogId
                    && item.Item.ClientMessageId == clientMessageId)
                .Select(item => item.Item)
                .ToList();

            return Task.FromResult(result);
        }

        public void Add(Guid ownerId, Guid dialogId, AttachmentMetadataResponse item)
        {
            _items.Add((ownerId, dialogId, item));
        }
    }
}