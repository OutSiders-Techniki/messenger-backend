using System.ComponentModel.DataAnnotations;

namespace Messenger.Application.DTOs;

public class RegisterRequest
{
    [Required(ErrorMessage = "Укажите пароль.")]
    [MinLength(8, ErrorMessage = "Пароль должен быть не короче 8 символов.")]
    public string? Password { get; init; }
}

public record RegisterResponse(string PublicId);
