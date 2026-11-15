using Messenger.Domain.Entities;

namespace Messenger.Application.Interfaces;

public interface ISessionStore
{
    Task<Session> CreateAsync(
        Guid userId,
        string tokenHash,
        DateTime expiresAt,
        CancellationToken cancellationToken = default);

    Task<Session?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Отзывает сессию. Возвращает false, если сессии уже не было.</summary>
    Task<bool> RevokeAsync(string tokenHash, CancellationToken cancellationToken = default);
}
