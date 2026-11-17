using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Messenger.Application.DTOs;
using Messenger.Application.Exceptions;
using Messenger.Application.Interfaces;
using Messenger.Domain.Entities;
using Messenger.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Repositories;

/// <summary>Все изменения и снимки истории сериализуются блокировкой строки диалога в PostgreSQL.</summary>
public sealed class MessageRepository(AppDbContext context) : IMessageRepository
{
    public async Task<MessageSendResult> SendAsync(Guid userId, Guid dialogId, SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var dialog = await LockDialogAsync(userId, dialogId, cancellationToken);
        var recipients = await ParticipantIdsAsync(dialogId, cancellationToken);
        if (recipients.Count != 2)
            throw new DialogNotFoundException();

        foreach (var ownerId in recipients)
        {
            context.Messages.Add(new Message
            {
                Id = Guid.NewGuid(), DialogId = dialogId, SenderId = userId, OwnerId = ownerId,
                ClientMessageId = request.Id, Version = request.Version, SentAt = request.SentAt.UtcDateTime,
                Iv = request.Iv!.ToArray(), Ciphertext = request.Ciphertext!.ToArray(),
            });
        }
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var packet = new MessageResponse(dialogId, userId, request.Version, request.Id, request.SentAt, request.Iv!, request.Ciphertext!);
        return new MessageSendResult(new SendMessageResponse(packet, true, false, dialog.MessageRevision), recipients);
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

}
