using Messenger.Application.DTOs;

namespace Messenger.Application.Interfaces;

public sealed record MessageBatchDeleteResult(
    IReadOnlyList<MessageDeletedEvent> DeletedMessages,
    IReadOnlyList<Guid> Recipients);

public interface IMessageBatchRepository
{
    /// <summary>Атомарно удаляет выбранные копии; null допустим только для очистки всей своей истории.</summary>
    Task<MessageBatchDeleteResult> DeleteBatchAsync(
        Guid userId,
        Guid dialogId,
        Guid[]? messageIds,
        bool forEveryone,
        CancellationToken cancellationToken = default);
}
