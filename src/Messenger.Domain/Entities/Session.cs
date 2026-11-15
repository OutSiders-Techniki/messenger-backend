namespace Messenger.Domain.Entities;

/// <summary>
/// Серверная сессия пользователя. Хранится только хеш токена —
/// сам токен уходит клиенту в HttpOnly-cookie.
/// </summary>
public class Session
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>SHA-256 хеш токена сессии (base64url).</summary>
    public required string TokenHash { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    public User User { get; set; } = null!;
}
