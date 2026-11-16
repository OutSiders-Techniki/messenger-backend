using System.Linq.Expressions;
using Messenger.Application.DTOs;
using Messenger.Application.Interfaces;
using Messenger.Domain.Entities;
using Messenger.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Repositories;

public class AttachmentRepository(AppDbContext context) : IAttachmentRepository
{
    /// <summary>
    /// Проекция в DTO: наружу уходит только то, что знает сервер, плюс
    /// зашифрованные клиентом метаданные. Ключ хранилища и хеши не грузятся.
    /// Клиентские идентификаторы одинаковы у копий сообщения у обоих
    /// участников — по ним обе стороны собирают AAD одинаково.
    /// </summary>
    private static readonly Expression<Func<
        Attachment, AttachmentMetadataResponse>> Projection =
        a => new AttachmentMetadataResponse(
            a.Message.ClientMessageId,
            a.ClientAttachmentId,
            a.Size,
            a.UploadedAt,
            a.EncryptedMetadata);

    public async Task<AttachmentMetadataResponse?> FindForOwnerAsync(
        Guid dialogId,
        Guid clientMessageId,
        Guid clientAttachmentId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Вложение принадлежит копии сообщения: доступ у владельца этой
        // копии. Ключ хранилища наружу не отдаём — он внутренний.
        return await context.Attachments
            .AsNoTracking()
            .Where(a =>
                a.Message.DialogId == dialogId
                && a.Message.ClientMessageId == clientMessageId
                && a.ClientAttachmentId == clientAttachmentId
                && a.Message.OwnerId == userId)
            .Select(Projection)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AttachmentMetadataResponse>> ListForOwnerAsync(
        Guid dialogId,
        Guid clientMessageId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await context.Attachments
            .AsNoTracking()
            .Where(a =>
                a.Message.DialogId == dialogId
                && a.Message.ClientMessageId == clientMessageId
                && a.Message.OwnerId == userId)
            .OrderBy(a => a.UploadedAt)
            .Select(Projection)
            .ToListAsync(cancellationToken);
    }
}