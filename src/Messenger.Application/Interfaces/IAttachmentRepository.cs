using Messenger.Application.DTOs;
using Messenger.Domain.Entities;

namespace Messenger.Application.Interfaces;

public interface IAttachmentRepository
{
    /// <summary>
    /// Метаданные вложения — только владельцу копии сообщения, к которому
    /// вложение относится. Для постороннего результат такой же, как для
    /// несуществующего вложения.
    /// </summary>
    Task<AttachmentMetadataResponse?> FindForOwnerAsync(
        Guid dialogId,
        Guid clientMessageId,
        Guid clientAttachmentId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Метаданные всех вложений копии сообщения. Чужие копии в выборку
    /// не попадают.
    /// </summary>
    Task<IReadOnlyList<AttachmentMetadataResponse>> ListForOwnerAsync(
        Guid dialogId,
        Guid clientMessageId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Идентификатор копии сообщения, принадлежащей пользователю. Если
    /// такой копии нет — результат <c>null</c>: постороннему сообщение
    /// не раскрывается.
    /// </summary>
    Task<Guid?> FindMessageIdForOwnerAsync(
        Guid dialogId,
        Guid clientMessageId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Есть ли уже вложение с таким клиентским идентификатором в копии
    /// сообщения пользователя.
    /// </summary>
    Task<bool> ExistsForOwnerAsync(
        Guid dialogId,
        Guid clientMessageId,
        Guid clientAttachmentId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ключ содержимого вложения в хранилище — только владельцу копии
    /// сообщения. Для постороннего результат такой же, как для
    /// несуществующего вложения.
    /// </summary>
    Task<string?> FindStorageKeyForOwnerAsync(
        Guid dialogId,
        Guid clientMessageId,
        Guid clientAttachmentId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет вложение в копии сообщения. Повторная загрузка того же
    /// клиентского идентификатора в ту же копию — конфликт.
    /// </summary>
    Task AddAsync(Attachment attachment, CancellationToken cancellationToken = default);
}