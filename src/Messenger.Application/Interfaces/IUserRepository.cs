using Messenger.Application.Exceptions;
using Messenger.Domain.Entities;

namespace Messenger.Application.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByPublicIdAsync(string publicId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Добавляет пользователя. Бросает <see cref="DuplicatePublicIdException"/>,
    /// если публичный ID уже занят.
    /// </summary>
    Task AddAsync(User user, CancellationToken cancellationToken = default);
}
