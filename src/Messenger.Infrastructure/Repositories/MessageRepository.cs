using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Messenger.Application.DTOs;
using Messenger.Application.Exceptions;
using Messenger.Application.Interfaces;
using Messenger.Domain.Entities;
using Messenger.Infrastructure.Db;
using Messenger.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Repositories;

/// <summary>Все изменения и снимки истории сериализуются блокировкой строки диалога в PostgreSQL.</summary>
public sealed class MessageRepository(AppDbContext context, FileDeletionProcessor cleanup) : IMessageRepository
{
    public async Task<MessageSendResult> SendAsync(Guid userId, Guid dialogId, SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var dialog = await LockDialogAsync(userId, dialogId, cancellationToken);
        var recipients = await ParticipantIdsAsync(dialogId, cancellationToken);
        if (recipients.Count != 2)
            throw new DialogNotFoundException();

        var fingerprint = Fingerprint(dialogId, userId, request);
        var receipt = await context.MessageReceipts.FindAsync([dialogId, request.Id], cancellationToken);
        if (receipt is null)
        {
            // Совместимость с сообщениями, созданными до миграции протокола.
            var old = await context.Messages.AsNoTracking()
                .FirstOrDefaultAsync(x => x.DialogId == dialogId && x.ClientMessageId == request.Id, cancellationToken);
            if (old is not null)
            {
                receipt = ReceiptFor(old, dialog.MessageRevision);
                context.MessageReceipts.Add(receipt);
                await context.SaveChangesAsync(cancellationToken);
            }
        }

        if (receipt is not null)
        {
            if (receipt.SenderId != userId || !CryptographicOperations.FixedTimeEquals(receipt.Fingerprint, fingerprint))
                throw new MessageConflictException();
            var copy = await context.Messages.AsNoTracking().SingleOrDefaultAsync(
                x => x.DialogId == dialogId && x.OwnerId == userId && x.ClientMessageId == request.Id, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new MessageSendResult(
                new SendMessageResponse(copy is null ? null : ToResponse(copy), false, copy is null, dialog.MessageRevision), []);
        }

        foreach (var ownerId in recipients)
        {
            context.Messages.Add(new Message
            {
                Id = Guid.NewGuid(), DialogId = dialogId, SenderId = userId, OwnerId = ownerId,
                ClientMessageId = request.Id, Version = request.Version, SentAt = request.SentAt.UtcDateTime,
                Iv = request.Iv!.ToArray(), Ciphertext = request.Ciphertext!.ToArray(),
            });
        }
        context.MessageReceipts.Add(new MessageReceipt
        {
            DialogId = dialogId, ClientMessageId = request.Id, SenderId = userId,
            Fingerprint = fingerprint, Revision = dialog.MessageRevision,
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var packet = new MessageResponse(dialogId, userId, request.Version, request.Id, request.SentAt, request.Iv!, request.Ciphertext!);
        return new MessageSendResult(new SendMessageResponse(packet, true, false, dialog.MessageRevision), recipients);
    }

    public async Task<MessageHistoryResponse> GetHistoryAsync(Guid userId, Guid dialogId, int limit, string? before, CancellationToken cancellationToken = default)
    {
        var cursor = ParseCursor(before);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var dialog = await LockDialogAsync(userId, dialogId, cancellationToken);
        var query = context.Messages.AsNoTracking().Where(x => x.DialogId == dialogId && x.OwnerId == userId);
        if (cursor is not null)
        {
            var (sentAt, id) = cursor.Value;
            query = query.Where(x => x.SentAt < sentAt || (x.SentAt == sentAt && x.ClientMessageId.CompareTo(id) < 0));
        }
        var rows = await query.OrderByDescending(x => x.SentAt).ThenByDescending(x => x.ClientMessageId)
            .Take(limit + 1).ToListAsync(cancellationToken);
        var more = rows.Count > limit;
        var items = rows.Take(limit).Select(ToResponse).ToArray();
        var next = more ? EncodeCursor(rows[limit - 1]) : null;
        await transaction.CommitAsync(cancellationToken);
        return new MessageHistoryResponse(items, next, dialog.MessageRevision);
    }

    public async Task<MessageDeleteResult> DeleteAsync(Guid userId, Guid dialogId, Guid messageId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var dialog = await LockDialogAsync(userId, dialogId, cancellationToken);
        var rows = await context.Messages
            .Where(x => x.DialogId == dialogId && x.ClientMessageId == messageId && x.OwnerId == userId)
            .Include(x => x.Attachments).ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return new MessageDeleteResult(false, dialog.MessageRevision, []);
        }

        // Рецепт сохраняется до удаления последней рабочей копии, в том числе для старых данных.
        if (!await context.MessageReceipts.AnyAsync(x => x.DialogId == dialogId && x.ClientMessageId == messageId, cancellationToken))
            context.MessageReceipts.Add(ReceiptFor(rows[0], dialog.MessageRevision));
        var storageKeys = rows.SelectMany(x => x.Attachments).Select(x => x.StorageKey).Distinct().ToArray();
        context.Messages.RemoveRange(rows);
        await context.SaveChangesAsync(cancellationToken);
        foreach (var key in storageKeys)
        {
            // Файл может использовать оставшаяся копия: пока есть ссылка, он нужен.
            if (!await context.Attachments.AnyAsync(x => x.StorageKey == key, cancellationToken)
                && !await context.PendingFileDeletions.AnyAsync(x => x.StorageKey == key, cancellationToken))
                context.PendingFileDeletions.Add(new PendingFileDeletion { StorageKey = key });
        }
        await context.SaveChangesAsync(cancellationToken);
        IReadOnlyList<Guid> recipients = [userId];
        await transaction.CommitAsync(cancellationToken);
        await cleanup.ProcessAsync(cancellationToken);
        return new MessageDeleteResult(true, dialog.MessageRevision, recipients);
    }

    private async Task<Dialog> LockDialogAsync(Guid userId, Guid dialogId, CancellationToken cancellationToken)
    {
        // Параметризованный SQL; посторонний не блокирует чужой диалог и не узнаёт, существует ли он.
        return await context.Dialogs.FromSqlInterpolated($"""
            SELECT d.* FROM dialogs AS d
            WHERE d.id = {dialogId}
              AND EXISTS (SELECT 1 FROM dialog_participants AS p WHERE p.dialog_id = d.id AND p.user_id = {userId})
            FOR UPDATE
            """).SingleOrDefaultAsync(cancellationToken) ?? throw new DialogNotFoundException();
    }

    private async Task<List<Guid>> ParticipantIdsAsync(Guid dialogId, CancellationToken cancellationToken) =>
        await context.DialogParticipants.Where(x => x.DialogId == dialogId).Select(x => x.UserId).ToListAsync(cancellationToken);

    private static MessageResponse ToResponse(Message row) => new(
        row.DialogId, row.SenderId, row.Version, row.ClientMessageId,
        new DateTimeOffset(DateTime.SpecifyKind(row.SentAt, DateTimeKind.Utc)), row.Iv, row.Ciphertext);

    private static MessageReceipt ReceiptFor(Message row, long revision) => new()
    {
        DialogId = row.DialogId, ClientMessageId = row.ClientMessageId, SenderId = row.SenderId, Revision = revision,
        Fingerprint = Fingerprint(row.DialogId, row.SenderId,
            new SendMessageRequest(row.Version, row.ClientMessageId,
                new DateTimeOffset(DateTime.SpecifyKind(row.SentAt, DateTimeKind.Utc)), row.Iv, row.Ciphertext)),
    };

    private static byte[] Fingerprint(Guid dialogId, Guid senderId, SendMessageRequest request)
    {
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(dialogId.ToByteArray()); writer.Write(senderId.ToByteArray());
            writer.Write(request.Id.ToByteArray()); writer.Write(request.Version);
            writer.Write(request.SentAt.UtcTicks); writer.Write(request.Iv!); writer.Write(request.Ciphertext!);
        }
        return SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }

    private static string EncodeCursor(Message row) => Convert.ToBase64String(Encoding.ASCII.GetBytes(
        row.SentAt.Ticks.ToString(CultureInfo.InvariantCulture) + ":" + row.ClientMessageId.ToString("D")))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static (DateTime SentAt, Guid Id)? ParseCursor(string? cursor)
    {
        if (cursor is null) return null;
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
            var parts = Encoding.ASCII.GetString(Convert.FromBase64String(base64)).Split(':');
            if (parts.Length != 2 || !long.TryParse(parts[0], CultureInfo.InvariantCulture, out var ticks)
                || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks
                || !Guid.TryParseExact(parts[1], "D", out var id) || id == Guid.Empty)
                throw new FormatException();
            return (new DateTime(ticks, DateTimeKind.Utc), id);
        }
        catch (FormatException)
        {
            throw new ValidationException("Некорректный курсор истории.");
        }
    }
}
