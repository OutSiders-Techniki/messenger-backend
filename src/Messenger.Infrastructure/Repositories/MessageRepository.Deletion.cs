using Messenger.Application.DTOs;
using Messenger.Application.Exceptions;
using Messenger.Application.Interfaces;
using Messenger.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Repositories;

public sealed partial class MessageRepository
{
    public async Task<MessageBatchDeleteResult> DeleteBatchAsync(
        Guid userId, Guid dialogId, Guid[]? messageIds, bool forEveryone, CancellationToken cancellationToken = default)
    {
        // Не допускаем неявную очистку обеих сторон даже при вызове репозитория без HTTP-слоя.
        if (forEveryone && (messageIds is null || messageIds.Length == 0))
            throw new ValidationException("Укажите сообщения для удаления.");
        if (messageIds is { Length: > MessageLimits.MaxSyncIds } || messageIds?.Contains(Guid.Empty) == true)
            throw new ValidationException("Передайте не более 500 непустых идентификаторов сообщений.");

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var dialog = await LockDialogAsync(userId, dialogId, cancellationToken);
        var query = context.Messages.Where(x => x.DialogId == dialogId && (forEveryone || x.OwnerId == userId));
        if (messageIds is not null)
            query = query.Where(x => messageIds.Contains(x.ClientMessageId));
        var rows = await query.Include(x => x.Attachments).ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            // Удаление пришло раньше пакетов отправки: отметки не дадут поздней
            // отправке воскресить копии.
            if (messageIds is not null)
            {
                await RecordDeletionsAsync(dialogId, messageIds,
                    forEveryone ? await ParticipantIdsAsync(dialogId, cancellationToken) : [userId], cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return new MessageBatchDeleteResult([], []);
        }

        var ids = rows.Select(x => x.ClientMessageId).Distinct().Order().ToArray();
        var existingReceipts = await context.MessageReceipts.AsNoTracking()
            .Where(x => x.DialogId == dialogId && ids.Contains(x.ClientMessageId))
            .Select(x => x.ClientMessageId).ToListAsync(cancellationToken);
        var receiptIds = existingReceipts.ToHashSet();
        var firstCopyById = rows.GroupBy(x => x.ClientMessageId).ToDictionary(x => x.Key, x => x.First());
        var deletedEvents = new List<MessageDeletedEvent>(ids.Length);
        foreach (var id in ids)
        {
            dialog.MessageRevision++;
            // Старые сообщения тоже получают минимальный отпечаток до удаления последней копии.
            if (!receiptIds.Contains(id))
                context.MessageReceipts.Add(ReceiptFor(firstCopyById[id], dialog.MessageRevision));
            deletedEvents.Add(new MessageDeletedEvent(dialogId, id, forEveryone, dialog.MessageRevision));
        }

        // Отметки об удалении нужны и для уже удалённых копий: поздняя отправка
        // с тем же идентификатором не должна воскресить переписку.
        await RecordDeletionsAsync(dialogId, ids,
            forEveryone ? await ParticipantIdsAsync(dialogId, cancellationToken) : [userId], cancellationToken);

        var storageKeys = rows.SelectMany(x => x.Attachments).Select(x => x.StorageKey).Distinct().ToArray();
        context.Messages.RemoveRange(rows);
        await context.SaveChangesAsync(cancellationToken);

        // Сохраняем очередь уборки в той же транзакции. Файл выжившей копии не трогаем.
        var referencedKeys = await context.Attachments.AsNoTracking()
            .Where(x => storageKeys.Contains(x.StorageKey)).Select(x => x.StorageKey).Distinct().ToListAsync(cancellationToken);
        var queuedKeys = await context.PendingFileDeletions.AsNoTracking()
            .Where(x => storageKeys.Contains(x.StorageKey)).Select(x => x.StorageKey).ToListAsync(cancellationToken);
        foreach (var key in storageKeys.Except(referencedKeys).Except(queuedKeys))
            context.PendingFileDeletions.Add(new PendingFileDeletion { StorageKey = key });
        await context.SaveChangesAsync(cancellationToken);

        IReadOnlyList<Guid> recipients = forEveryone ? await ParticipantIdsAsync(dialogId, cancellationToken) : [userId];
        await transaction.CommitAsync(cancellationToken);
        await cleanup.ProcessAsync(cancellationToken);
        return new MessageBatchDeleteResult(deletedEvents, recipients);
    }
}
