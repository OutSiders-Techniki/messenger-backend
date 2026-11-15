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
