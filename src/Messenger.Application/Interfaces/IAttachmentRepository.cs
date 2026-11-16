using Messenger.Application.DTOs;

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
}