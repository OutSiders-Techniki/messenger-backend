using Messenger.Application.Interfaces;
using Messenger.Domain.Entities;
using Messenger.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Repositories;

public class SessionRepository(AppDbContext context) : ISessionStore
{
    public async Task<Session> CreateAsync(
        Guid userId,
        string tokenHash,
        DateTime expiresAt,
        CancellationToken cancellationToken = default)
    {
        // Чистим протухшие сессии, иначе таблица растёт бесконечно.
        await context.Sessions
            .Where(s => s.ExpiresAt <= DateTime.UtcNow)
            .ExecuteDeleteAsync(cancellationToken);

        var session = new Session
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
        };

        context.Sessions.Add(session);
        await context.SaveChangesAsync(cancellationToken);

        return session;
    }

    public Task<Session?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        return context.Sessions
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.TokenHash == tokenHash, cancellationToken);
    }

    /// <summary>
    /// Отзыв одним DELETE-запросом: конкурентные выходы не конфликтуют
    /// и не приводят к ошибке «ожидалась 1 строка, изменено 0».
    /// </summary>
    public async Task<bool> RevokeAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        var deleted = await context.Sessions
            .Where(s => s.TokenHash == tokenHash)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted > 0;
    }
}
