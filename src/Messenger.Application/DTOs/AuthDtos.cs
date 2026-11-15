using System.ComponentModel.DataAnnotations;

namespace Messenger.Application.DTOs;

public class LoginRequest
{
    [Required(ErrorMessage = "Укажите публичный ID.")]
    [MaxLength(32, ErrorMessage = "Публичный ID не может быть длиннее 32 символов.")]
    public string? PublicId { get; init; }

    [Required(ErrorMessage = "Укажите пароль.")]
    [MinLength(8, ErrorMessage = "Пароль должен быть не короче 8 символов.")]
    public string? Password { get; init; }
}

public record LoginResponse(string PublicId);

public record CurrentUserResponse(string PublicId, DateTime CreatedAt);
