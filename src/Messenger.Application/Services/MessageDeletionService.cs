using Messenger.Application.DTOs;
using Messenger.Application.Exceptions;
using Messenger.Application.Interfaces;

namespace Messenger.Application.Services;

public sealed class MessageDeletionService(IMessageBatchRepository messages, IMessageEvents events)
{
    public Task DeleteSelfAsync(
        Guid userId, Guid dialogId, DeleteSelfMessagesRequest request, CancellationToken cancellationToken = default) =>
        DeleteAsync(userId, dialogId, request.MessageIds, forEveryone: false, cancellationToken);

    public Task DeleteBothAsync(
        Guid userId, Guid dialogId, DeleteBothMessagesRequest request, CancellationToken cancellationToken = default) =>
        DeleteAsync(userId, dialogId, request.MessageIds, forEveryone: true, cancellationToken);

    private async Task DeleteAsync(
        Guid userId, Guid dialogId, Guid[]? ids, bool forEveryone, CancellationToken cancellationToken)
    {
        if (forEveryone && (ids is null || ids.Length == 0))
            throw new ValidationException("Укажите сообщения для удаления.");
        if (ids is { Length: > MessageLimits.MaxSyncIds } || ids?.Contains(Guid.Empty) == true)
            throw new ValidationException("Передайте не более 500 непустых идентификаторов сообщений.");

        var result = await messages.DeleteBatchAsync(userId, dialogId, ids?.Distinct().ToArray(), forEveryone, cancellationToken);
        // Ревизии и получатели определены зафиксированной транзакцией, не данными клиента.
        foreach (var deleted in result.DeletedMessages.OrderBy(x => x.Revision))
            await events.DeletedAsync(result.Recipients, deleted, cancellationToken);
    }
}
