using Messenger.Application.DTOs;
using Messenger.Application.Exceptions;
using Messenger.Application.Interfaces;

namespace Messenger.Application.Services;

/// <summary>
/// Диалоги один-на-один. Диалог между парой пользователей единственный:
/// повторный запрос возвращает существующий, а не создаёт дубль.
/// </summary>
public class DialogService(IUserRepository users, IDialogRepository dialogs)
{
    public async Task<DialogCreationResult> CreateAsync(
        Guid creatorId,
        string companionPublicId,
        CancellationToken cancellationToken = default)
    {
        var publicId = companionPublicId?.Trim();

        if (string.IsNullOrEmpty(publicId))
        {
            throw new ValidationException("Укажите публичный ID собеседника.");
        }

        var companion = await users.GetByPublicIdAsync(publicId, cancellationToken);

        if (companion is null)
        {
            throw new UserNotFoundException();
        }

        if (companion.Id == creatorId)
        {
            throw new ValidationException("Нельзя создать диалог с самим собой.");
        }

        var lookup = await dialogs.GetOrCreateAsync(creatorId, companion.Id, cancellationToken);

        return new DialogCreationResult(
            new DialogResponse(lookup.Dialog.Id, companion.PublicId, lookup.Dialog.CreatedAt),
            lookup.Created);
    }

    /// <summary>
    /// Список диалогов пользователя. Собеседником считается второй
    /// участник; чужие диалоги в список не попадают.
    /// </summary>
    public async Task<IReadOnlyList<DialogListItem>> GetListAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await dialogs.GetForUserAsync(userId, cancellationToken);
    }

    /// <summary>
    /// Открытие диалога. Доступ только у участников: постороннему диалог
    /// недоступен, и сам факт его существования не раскрывается.
    /// </summary>
    public async Task<DialogListItem> GetByIdAsync(
        Guid userId,
        Guid dialogId,
        CancellationToken cancellationToken = default)
    {
        var dialog = await dialogs.FindForParticipantAsync(dialogId, userId, cancellationToken);

        if (dialog is null)
        {
            throw new DialogNotFoundException();
        }

        return dialog;
    }
}