using System.Linq.Expressions;
using Messenger.Application.DTOs;
using Messenger.Application.Exceptions;
using Messenger.Application.Interfaces;
using Messenger.Domain.Entities;
using Messenger.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

    public async Task<Guid?> FindMessageIdForOwnerAsync(
        Guid dialogId,
        Guid clientMessageId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await context.Messages
            .AsNoTracking()
            .Where(m =>
                m.DialogId == dialogId
                && m.ClientMessageId == clientMessageId
                && m.OwnerId == userId)
            .Select(m => (Guid?)m.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> ExistsForOwnerAsync(
        Guid dialogId,
        Guid clientMessageId,
        Guid clientAttachmentId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await context.Attachments
            .AsNoTracking()
            .AnyAsync(a =>
                    a.Message.DialogId == dialogId
                    && a.Message.ClientMessageId == clientMessageId
                    && a.ClientAttachmentId == clientAttachmentId
                    && a.Message.OwnerId == userId,
                cancellationToken);
    }

    public async Task<string?> FindStorageKeyForOwnerAsync(
        Guid dialogId,
        Guid clientMessageId,
        Guid clientAttachmentId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Ключ хранилища наружу не отдаём — он внутренний и нужен только
        // для чтения содержимого.
        return await context.Attachments
            .AsNoTracking()
            .Where(a =>
                a.Message.DialogId == dialogId
                && a.Message.ClientMessageId == clientMessageId
                && a.ClientAttachmentId == clientAttachmentId
                && a.Message.OwnerId == userId)
            .Select(a => a.StorageKey)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(
        Attachment attachment,
        CancellationToken cancellationToken = default)
    {
        context.Attachments.Add(attachment);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // Клиентский идентификатор вложения уникален в копии сообщения:
            // повторная загрузка — конфликт, а не новое вложение.
            context.Entry(attachment).State = EntityState.Detached;

            throw new DuplicateAttachmentException();
        }
    }
}