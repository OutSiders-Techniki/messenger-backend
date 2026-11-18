using Messenger.Application;
using Messenger.Application.DTOs;
using Messenger.Application.Exceptions;
using Messenger.Application.Interfaces;
using Messenger.Application.Services;
using Messenger.Domain.Entities;

namespace Messenger.Tests.Services;

public class AttachmentServiceTests
{
    private readonly FakeAttachmentRepository _attachments = new();
    private readonly FakeContentStore _contentStore = new();

    private AttachmentService CreateSut() => new(_attachments, _contentStore);

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

    [Fact]
    public async Task UploadAsync_ForOwner_SavesContentAndMetadata()
    {
        var ownerId = Guid.NewGuid();
        var dialogId = Guid.NewGuid();
        var clientMessageId = Guid.NewGuid();
        var clientAttachmentId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        _attachments.AddMessageCopy(ownerId, dialogId, clientMessageId, messageId);

        var sut = CreateSut();

        var uploaded = await sut.UploadAsync(
            ownerId,
            dialogId,
            clientMessageId,
            clientAttachmentId,
            [7, 8, 9],
            new MemoryStream([1, 2, 3, 4]));

        Assert.Equal(clientMessageId, uploaded.ClientMessageId);
        Assert.Equal(clientAttachmentId, uploaded.ClientAttachmentId);
        Assert.Equal(4, uploaded.Size);
        Assert.Equal(new byte[] { 7, 8, 9 }, uploaded.EncryptedMetadata);

        // Содержимое сохранено целиком.
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, _contentStore.SavedContent);
    }

    [Fact]
    public async Task UploadAsync_ForStranger_ThrowsNotFound()
    {
        var dialogId = Guid.NewGuid();
        var clientMessageId = Guid.NewGuid();

        _attachments.AddMessageCopy(
            Guid.NewGuid(),
            dialogId,
            clientMessageId,
            Guid.NewGuid());

        var sut = CreateSut();

        await Assert.ThrowsAsync<AttachmentNotFoundException>(() => sut.UploadAsync(
            Guid.NewGuid(),
            dialogId,
            clientMessageId,
            Guid.NewGuid(),
            [1],
            new MemoryStream([1, 2, 3])));

        Assert.Null(_contentStore.SavedContent);
    }

    [Fact]
    public async Task UploadAsync_WithEmptyClientId_ThrowsValidation()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ValidationException>(() => sut.UploadAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.Empty,
            [1],
            new MemoryStream([1])));
    }

    [Fact]
    public async Task UploadAsync_WithoutMetadata_ThrowsValidation()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ValidationException>(() => sut.UploadAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            [],
            new MemoryStream([1])));
    }

    [Fact]
    public async Task UploadAsync_WithEmptyContent_ThrowsValidation()
    {
        var ownerId = Guid.NewGuid();
        var dialogId = Guid.NewGuid();
        var clientMessageId = Guid.NewGuid();

        _attachments.AddMessageCopy(ownerId, dialogId, clientMessageId, Guid.NewGuid());

        var sut = CreateSut();

        await Assert.ThrowsAsync<ValidationException>(() => sut.UploadAsync(
            ownerId,
            dialogId,
            clientMessageId,
            Guid.NewGuid(),
            [1],
            new MemoryStream([])));
    }

    [Fact]
    public async Task UploadAsync_LargerThanLimit_ThrowsTooLarge()
    {
        var ownerId = Guid.NewGuid();
        var dialogId = Guid.NewGuid();
        var clientMessageId = Guid.NewGuid();

        _attachments.AddMessageCopy(ownerId, dialogId, clientMessageId, Guid.NewGuid());

        _contentStore.MaxBytes = 4;

        var sut = CreateSut();

        await Assert.ThrowsAsync<AttachmentTooLargeException>(() => sut.UploadAsync(
            ownerId,
            dialogId,
            clientMessageId,
            Guid.NewGuid(),
            [1],
            new MemoryStream([1, 2, 3, 4, 5])));
    }

    [Fact]
    public async Task UploadAsync_WithExistingClientId_ThrowsDuplicate()
    {
        var ownerId = Guid.NewGuid();
        var dialogId = Guid.NewGuid();
        var clientMessageId = Guid.NewGuid();
        var clientAttachmentId = Guid.NewGuid();

        _attachments.AddMessageCopy(ownerId, dialogId, clientMessageId, Guid.NewGuid());
        _attachments.Add(ownerId, dialogId, new AttachmentMetadataResponse(
            clientMessageId,
            clientAttachmentId,
            1024,
            DateTime.UtcNow,
            [1]));

        var sut = CreateSut();

        await Assert.ThrowsAsync<DuplicateAttachmentException>(() => sut.UploadAsync(
            ownerId,
            dialogId,
            clientMessageId,
            clientAttachmentId,
            [1],
            new MemoryStream([1, 2, 3])));

        // Дубль отсекается до записи байтов.
        Assert.Null(_contentStore.SavedContent);
    }

    [Fact]
    public async Task UploadAsync_WithTooLargeMetadata_ThrowsValidation()
    {
        var ownerId = Guid.NewGuid();
        var dialogId = Guid.NewGuid();
        var clientMessageId = Guid.NewGuid();

        _attachments.AddMessageCopy(ownerId, dialogId, clientMessageId, Guid.NewGuid());

        var sut = CreateSut();

        await Assert.ThrowsAsync<ValidationException>(() => sut.UploadAsync(
            ownerId,
            dialogId,
            clientMessageId,
            Guid.NewGuid(),
            new byte[MediaLimits.MaxAttachmentMetadataBytes + 1],
            new MemoryStream([1])));
    }

    private sealed class FakeContentStore : IAttachmentContentStore
    {
        public long MaxBytes { get; set; } = MediaLimits.MaxAttachmentSizeBytes;

        public byte[]? SavedContent { get; private set; }

        public Task<StoredContent> SaveAsync(
            Stream content,
            long maxBytes,
            CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);

            var bytes = buffer.ToArray();

            if (bytes.Length > Math.Min(maxBytes, MaxBytes))
            {
                throw new AttachmentTooLargeException(maxBytes);
            }

            SavedContent = bytes;

            return Task.FromResult(new StoredContent("key", bytes.Length));
        }

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Stream?>(SavedContent is null ? null : new MemoryStream(SavedContent));
        }
    }

    private sealed class FakeAttachmentRepository : IAttachmentRepository
    {
        public string? StorageKey { get; set; }

        private readonly List<(Guid OwnerId, Guid DialogId, AttachmentMetadataResponse Item)> _items = new();

        private readonly List<(Guid OwnerId, Guid DialogId, Guid ClientMessageId, Guid MessageId)>
            _messages = new();

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

        public Task<Guid?> FindMessageIdForOwnerAsync(
            Guid dialogId,
            Guid clientMessageId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            foreach (var (ownerId, itemDialogId, itemClientMessageId, messageId) in _messages)
            {
                if (itemDialogId == dialogId
                    && itemClientMessageId == clientMessageId
                    && ownerId == userId)
                {
                    return Task.FromResult<Guid?>(messageId);
                }
            }

            return Task.FromResult<Guid?>(null);
        }

        public Task<bool> ExistsForOwnerAsync(
            Guid dialogId,
            Guid clientMessageId,
            Guid clientAttachmentId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            foreach (var (ownerId, itemDialogId, item) in _items)
            {
                if (item.ClientMessageId == clientMessageId
                    && item.ClientAttachmentId == clientAttachmentId
                    && itemDialogId == dialogId
                    && ownerId == userId)
                {
                    return Task.FromResult(true);
                }
            }

            return Task.FromResult(false);
        }

        public Task<string?> FindStorageKeyForOwnerAsync(
            Guid dialogId,
            Guid clientMessageId,
            Guid clientAttachmentId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            foreach (var (ownerId, itemDialogId, item) in _items)
            {
                if (item.ClientMessageId == clientMessageId
                    && item.ClientAttachmentId == clientAttachmentId
                    && itemDialogId == dialogId
                    && ownerId == userId)
                {
                    return Task.FromResult(StorageKey);
                }
            }

            return Task.FromResult<string?>(null);
        }

        public Task AddAsync(
            Attachment attachment,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void Add(Guid ownerId, Guid dialogId, AttachmentMetadataResponse item)
        {
            _items.Add((ownerId, dialogId, item));
        }

        public void AddMessageCopy(
            Guid ownerId,
            Guid dialogId,
            Guid clientMessageId,
            Guid messageId)
        {
            _messages.Add((ownerId, dialogId, clientMessageId, messageId));
        }
    }
}