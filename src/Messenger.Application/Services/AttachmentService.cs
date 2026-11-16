using Messenger.Application.DTOs;
using Messenger.Application.Exceptions;
using Messenger.Application.Interfaces;

namespace Messenger.Application.Services;

/// <summary>
/// Метаданные зашифрованных вложений. Сервер отдаёт только то, что знает:
/// размер, момент загрузки и зашифрованные клиентом метаданные. Исходное
/// имя файла и открытое содержимое серверу недоступны.
/// </summary>
public class AttachmentService(IAttachmentRepository attachments)
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
}