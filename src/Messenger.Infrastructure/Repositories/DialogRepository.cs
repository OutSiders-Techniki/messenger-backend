using System.Linq.Expressions;
using Messenger.Application.DTOs;
using Messenger.Application.Exceptions;
using Messenger.Application.Interfaces;
using Messenger.Domain.Entities;
using Messenger.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Messenger.Infrastructure.Repositories;

public class DialogRepository(AppDbContext context) : IDialogRepository
{
    public async Task<DialogLookup> GetOrCreateAsync(
        Guid firstUserId,
        Guid secondUserId,
        CancellationToken cancellationToken = default)
    {
        var pairKey = Dialog.PairKeyFor(firstUserId, secondUserId);

        var existing = await FindByPairKeyAsync(pairKey, cancellationToken);

        if (existing is not null)
        {
            return new DialogLookup(existing, Created: false);
        }

        var dialog = new Dialog
        {
            Id = Guid.NewGuid(),
            PairKey = pairKey,
            CreatedAt = DateTime.UtcNow,
        };

        dialog.Participants.Add(new DialogParticipant
        {
            DialogId = dialog.Id,
            UserId = firstUserId,
            JoinedAt = dialog.CreatedAt,
        });

        dialog.Participants.Add(new DialogParticipant
        {
            DialogId = dialog.Id,
            UserId = secondUserId,
            JoinedAt = dialog.CreatedAt,
        });

        context.Dialogs.Add(dialog);

        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return new DialogLookup(dialog, Created: true);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "ix_dialogs_pair_key",
            })
        {
            // Дубль по уникальному индексу: диалог создали параллельно.
            Detach(dialog);

            var concurrent = await FindByPairKeyAsync(pairKey, cancellationToken);

            if (concurrent is null)
            {
                throw new DuplicateDialogException();
            }

            return new DialogLookup(concurrent, Created: false);
        }
    }

    public async Task<IReadOnlyList<DialogListItem>> GetForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Проекция сразу в DTO: в память не попадают ни хеш пароля
        // собеседника, ни прочие поля пользователя.
        var items = await context.Dialogs
            .AsNoTracking()
            .Where(d => d.Participants.Any(p => p.UserId == userId))
            .OrderByDescending(d => d.CreatedAt)
            .Select(CompanionProjection(userId))
            .ToListAsync(cancellationToken);

        // Диалог без второго участника — аномалия данных, в список он не идёт.
        return items.Where(i => !string.IsNullOrEmpty(i.CompanionPublicId)).ToList();
    }

    public async Task<DialogListItem?> FindForParticipantAsync(
        Guid dialogId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var item = await context.Dialogs
            .AsNoTracking()
            .Where(d => d.Id == dialogId && d.Participants.Any(p => p.UserId == userId))
            .Select(CompanionProjection(userId))
            .SingleOrDefaultAsync(cancellationToken);

        if (item is null || string.IsNullOrEmpty(item.CompanionPublicId))
        {
            return null;
        }

        return item;
    }

    /// <summary>
    /// Проекция диалога в элемент списка: собеседник — второй участник,
    /// подпись — своя (owner_id = пользователь запроса). Общая для выборки
    /// списка и открытия диалога.
    /// </summary>
    private static Expression<Func<Dialog, DialogListItem>> CompanionProjection(Guid userId) =>
        dialog => new DialogListItem(
            dialog.Id,
            dialog.Participants
                .Where(p => p.UserId != userId)
                .Select(p => p.User.PublicId)
                .FirstOrDefault()!,
            dialog.CreatedAt,
            dialog.Labels
                .Where(l => l.OwnerId == userId)
                .Select(l => new DialogLabelView(l.Version, l.Iv, l.Ciphertext, l.UpdatedAt))
                .FirstOrDefault());

    /// <summary>
    /// Сохраняет подпись контакта (upsert одним INSERT ... ON CONFLICT):
    /// конкурентные сохранения одного владельца не конфликтуют на PK.
    /// Подпись принадлежит владельцу, перезапись чужой подписи невозможна
    /// по PK (dialog_id, owner_id).
    /// </summary>
    public async Task SaveLabelAsync(
        Guid userId, Guid dialogId, PutLabelRequest request, CancellationToken cancellationToken = default)
    {
        var isParticipant = await context.DialogParticipants
            .AnyAsync(p => p.DialogId == dialogId && p.UserId == userId, cancellationToken);

        if (!isParticipant)
        {
            throw new DialogNotFoundException();
        }

        var now = DateTime.UtcNow;
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dialog_labels (dialog_id, owner_id, version, iv, ciphertext, updated_at)
            VALUES ({dialogId}, {userId}, {request.Version}, {request.Iv}, {request.Ciphertext}, {now})
            ON CONFLICT (dialog_id, owner_id) DO UPDATE
            SET version = {request.Version}, iv = {request.Iv}, ciphertext = {request.Ciphertext}, updated_at = {now}
            """, cancellationToken);
    }

    /// <summary>Удаляет подпись контакта. Отсутствие подписи ошибкой не является.</summary>
    public async Task DeleteLabelAsync(Guid userId, Guid dialogId, CancellationToken cancellationToken = default)
    {
        var isParticipant = await context.DialogParticipants
            .AnyAsync(p => p.DialogId == dialogId && p.UserId == userId, cancellationToken);

        if (!isParticipant)
        {
            throw new DialogNotFoundException();
        }

        await context.DialogLabels
            .Where(l => l.DialogId == dialogId && l.OwnerId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private Task<Dialog?> FindByPairKeyAsync(string pairKey, CancellationToken cancellationToken)
    {
        return context.Dialogs
            .AsNoTracking()
            .Include(d => d.Participants)
            .SingleOrDefaultAsync(d => d.PairKey == pairKey, cancellationToken);
    }

    /// <summary>
    /// Снимаем с отслеживания созданный диалог и его участников:
    /// остальные сущности в контексте к этой ошибке отношения не имеют.
    /// </summary>
    private void Detach(Dialog dialog)
    {
        context.Entry(dialog).State = EntityState.Detached;

        foreach (var participant in dialog.Participants)
        {
            context.Entry(participant).State = EntityState.Detached;
        }
    }
}