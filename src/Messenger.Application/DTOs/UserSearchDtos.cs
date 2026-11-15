namespace Messenger.Application.DTOs;

/// <summary>
/// Данные найденного пользователя. Ничего лишнего: пароля и хеша здесь
/// быть не должно, найденному пользователю и так предстоит вход.
/// </summary>
public record UserSearchResponse(string PublicId, DateTime CreatedAt);
