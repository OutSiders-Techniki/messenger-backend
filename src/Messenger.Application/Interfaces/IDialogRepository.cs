using Messenger.Application.DTOs;
using Messenger.Domain.Entities;

namespace Messenger.Application.Interfaces;

/// <summary>Диалог и признак, что он только что создан.</summary>
public sealed record DialogLookup(Dialog Dialog, bool Created);

public interface IDialogRepository
{
    /// <summary>
    /// Возвращает диалог между двумя пользователями, создавая его при
    /// необходимости. Ключ пары считается внутри репозитория, поэтому
    /// дубль между теми же людьми не может появиться.
    /// </summary>
    Task<DialogLookup> GetOrCreateAsync(
        Guid firstUserId,
        Guid secondUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Диалоги, в которых участвует пользователь. Диалоги других
    /// участников в выборку не попадают.
    /// </summary>
    Task<IReadOnlyList<DialogListItem>> GetForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Диалог по идентификатору — только если пользователь в нём участвует.
    /// Для постороннего результат такой же, как для несуществующего диалога.
    /// </summary>
    Task<DialogListItem?> FindForParticipantAsync(
        Guid dialogId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Сохраняет подпись контакта пользователя (upsert). Не участник получает DialogNotFoundException.</summary>
    Task SaveLabelAsync(
        Guid userId,
        Guid dialogId,
        PutLabelRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Удаляет подпись контакта пользователя; идемпотентно.</summary>
    Task DeleteLabelAsync(Guid userId, Guid dialogId, CancellationToken cancellationToken = default);
}