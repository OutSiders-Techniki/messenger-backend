namespace Messenger.Application.DTOs;

/// <summary>
/// Диалог в списке: собеседник и дата создания.
/// CompanionPublicId заполняется вторым участником диалога; диалоги без
/// второго участника (аномалия данных) отбрасываются при выборке.
/// </summary>
public record DialogListItem(Guid Id, string CompanionPublicId, DateTime CreatedAt, DialogLabelView? Label = null);