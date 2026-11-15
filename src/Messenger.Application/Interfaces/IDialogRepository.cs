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
}
