using Messenger.Application.DTOs;
using Messenger.Application.Exceptions;
using Messenger.Application.Interfaces;
using Messenger.Domain.Entities;

namespace Messenger.Application.Services;

/// <summary>
/// Поиск пользователя по точному публичному ID. Частичное совпадение
/// не подходит: ID выдаётся целиком, и пользователь ищет именно его.
/// </summary>
public class UserSearchService(IUserRepository users)
{
    public async Task<UserSearchResponse> FindByPublicIdAsync(
        string publicId,
        CancellationToken cancellationToken = default)
    {
        var user = await users.GetByPublicIdAsync(publicId, cancellationToken);

        return user is null ? throw new UserNotFoundException() : new UserSearchResponse(user.PublicId, user.CreatedAt);
    }
}
