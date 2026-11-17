using Messenger.Application.DTOs;
using Messenger.Application.Exceptions;
using Messenger.Application.Interfaces;
using Messenger.Domain.Entities;

namespace Messenger.Application.Services;

/// <summary>
/// Метаданные зашифрованных вложений. Сервер отдаёт только то, что знает:
/// размер, момент загрузки и зашифрованные клиентом метаданные. Исходное
/// имя файла и открытое содержимое серверу недоступны.
/// </summary>
public class AttachmentService(
    IAttachmentRepository attachments,
    IAttachmentContentStore contentStore)
{
    /// <summary>
    /// Метаданные вложения по клиентским идентификаторам сообщения и
    /// вложения. Доступно только владельцу копии сообщения, к которому
    /// вложение относится.
    /// </summary>
    public async Task<AttachmentMetadataResponse> GetMetadataAsync(
        Guid userId,
        Guid dialogId,
        Guid clientMessageId,
        Guid clientAttachmentId,
        CancellationToken cancellationToken = default)
    {
        var metadata = await attachments.FindForOwnerAsync(
            dialogId,
            clientMessageId,
            clientAttachmentId,
            userId,
            cancellationToken);

        if (metadata is null)
        {
            throw new AttachmentNotFoundException();
        }

        return metadata;
    }

    /// <summary>
    /// Метаданные всех вложений копии сообщения. Пустой список, если у
    /// сообщения нет вложений; чужие копии не видны.
    /// </summary>
    public async Task<IReadOnlyList<AttachmentMetadataResponse>> ListAsync(
        Guid userId,
        Guid dialogId,
        Guid clientMessageId,
        CancellationToken cancellationToken = default)
    {
        return await attachments.ListForOwnerAsync(
            dialogId,
            clientMessageId,
            userId,
            cancellationToken);
    }

    /// <summary>
    /// Загружает зашифрованные байты вложения в свою копию сообщения.
    /// Сервер сохраняет только ciphertext и зашифрованные клиентом
    /// метаданные: имя файла и тип содержимого сюда не попадают и MIME
    /// клиента не считается доверенным.
    /// </summary>
    public async Task<AttachmentMetadataResponse> UploadAsync(
        Guid userId,
        Guid dialogId,
        Guid clientMessageId,
        Guid clientAttachmentId,
        byte[] encryptedMetadata,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        if (clientAttachmentId == Guid.Empty)
        {
            throw new ValidationException("Идентификатор вложения не может быть пустым.");
        }

        if (encryptedMetadata.Length == 0)
        {
            throw new ValidationException("Зашифрованные метаданные вложения обязательны.");
        }

        if (encryptedMetadata.Length > MediaLimits.MaxAttachmentMetadataBytes)
        {
            throw new ValidationException("Зашифрованные метаданные вложения слишком велики.");
        }

        var messageId = await attachments.FindMessageIdForOwnerAsync(
            dialogId,
            clientMessageId,
            userId,
            cancellationToken);

        if (messageId is null)
        {
            // Копии сообщения у пользователя нет: постороннему отвечаем так же,
            // как на несуществующее вложение.
            throw new AttachmentNotFoundException();
        }

        // Повторную загрузку того же идентификатора отсекаем до записи
        // байтов: иначе диск зря занимал бы файлы по 25 МБ.
        if (await attachments.ExistsForOwnerAsync(
                dialogId,
                clientMessageId,
                clientAttachmentId,
                userId,
                cancellationToken))
        {
            throw new DuplicateAttachmentException();
        }

        // Хранилище само удаляет недописанный файл, если поток прерван
        // или превышен лимит.
        var stored = await contentStore.SaveAsync(
            content,
            MediaLimits.MaxAttachmentSizeBytes,
            cancellationToken);

        if (stored.Size == 0)
        {
            await contentStore.DeleteAsync(stored.StorageKey, cancellationToken);

            throw new ValidationException("Файл вложения пуст.");
        }

        var attachment = new Attachment
        {
            Id = Guid.NewGuid(),
            MessageId = messageId.Value,
            ClientAttachmentId = clientAttachmentId,
            StorageKey = stored.StorageKey,
            Size = stored.Size,
            EncryptedMetadata = encryptedMetadata,
            UploadedAt = DateTime.UtcNow,
        };

        try
        {
            await attachments.AddAsync(attachment, cancellationToken);
        }
        catch
        {
            // Строка не записалась — файл тоже не оставляем.
            await contentStore.DeleteAsync(stored.StorageKey, cancellationToken);

            throw;
        }

        return new AttachmentMetadataResponse(
            clientMessageId,
            clientAttachmentId,
            attachment.Size,
            attachment.UploadedAt,
            attachment.EncryptedMetadata);
    }
}