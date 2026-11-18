using Messenger.Application.Interfaces;
using Messenger.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Messenger.Infrastructure.Services;

public sealed class FileDeletionProcessor(AppDbContext context, IAttachmentContentStore content, ILogger<FileDeletionProcessor> logger)
{
    public async Task ProcessAsync(CancellationToken cancellationToken = default)
    {
        List<string> keys;
        try
        {
            keys = await context.PendingFileDeletions.AsNoTracking()
                .OrderBy(x => x.StorageKey).Select(x => x.StorageKey).Take(100).ToListAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            // Удаление сообщений уже зафиксировано. Сбой уборки не должен скрыть его успех
            // и помешать отправке события удаления; задания остаются в рабочей БД.
            logger.LogWarning("Attachment cleanup deferred ({ErrorType})", ex.GetType().Name);
            return;
        }
        foreach (var key in keys)
        {
            try
            {
                // Ссылка выжившей копии всегда важнее задания уборки.
                if (!await context.Attachments.AnyAsync(x => x.StorageKey == key, cancellationToken))
                    await content.DeleteAsync(key, cancellationToken);
                await context.PendingFileDeletions.Where(x => x.StorageKey == key).ExecuteDeleteAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Attachment deletion deferred ({ErrorType})", ex.GetType().Name);
            }
        }
    }
}
