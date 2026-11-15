using System.ComponentModel.DataAnnotations;

namespace Messenger.Application.DTOs;

/// <summary>Запрос на создание диалога: публичный ID собеседника.</summary>
public class CreateDialogRequest
{
    [Required(ErrorMessage = "Укажите публичный ID собеседника.")]
    [MaxLength(32, ErrorMessage = "Публичный ID не может быть длиннее 32 символов.")]
    public string? PublicId { get; init; }
}

/// <summary>Созданный или уже существующий диалог.</summary>
public record DialogResponse(Guid Id, string CompanionPublicId, DateTime CreatedAt);

/// <summary>
/// Результат создания диалога. <see cref="Created"/> = false, если диалог
/// между этой парой уже был и новый не создавался.
/// </summary>
public record DialogCreationResult(DialogResponse Dialog, bool Created);
