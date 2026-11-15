namespace Messenger.Application.DTOs;

/// <summary>Диалог в списке: собеседник и дата создания.</summary>
public record DialogListItem(Guid Id, string CompanionPublicId, DateTime CreatedAt);