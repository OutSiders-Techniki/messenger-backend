using System.Security.Cryptography;
using Messenger.Application.DTOs;
using Messenger.Application.Exceptions;
using Messenger.Application.Interfaces;
using Messenger.Application.Options;
using Messenger.Domain.Entities;
using Microsoft.Extensions.Options;

namespace Messenger.Application.Services;

public class AuthService(
    IUserRepository users,
    ISessionStore sessions,
    ISessionTokenGenerator tokenGenerator,
    IPasswordHasher passwordHasher,
    IOptions<SessionCookieOptions> options)
{
    /// <summary>
    /// Хеш заведомо неверного пароля: проверяется, когда пользователь не найден,
    /// чтобы время ответа не выдавало факт существования публичного ID.
    /// Считается один раз тем же хешером, что и настоящие пароли.
    /// </summary>
    private static string? _dummyHash;
    private static readonly object DummyHashLock = new();

    public async Task<(string Token, string PublicId)> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var publicId = request.PublicId ?? string.Empty;
        var password = request.Password ?? string.Empty;

        var user = await users.GetByPublicIdAsync(publicId, cancellationToken);

        if (user is null)
        {
            passwordHasher.Verify(password, GetDummyHash());

            throw new InvalidCredentialsException();
        }

        if (!passwordHasher.Verify(password, user.PasswordHash))
        {
            throw new InvalidCredentialsException();
        }

        var token = tokenGenerator.Create();
        var expiresAt = DateTime.UtcNow.AddHours(options.Value.LifetimeHours);

        await sessions.CreateAsync(user.Id, token.Hash, expiresAt, cancellationToken);

        return (token.Value, user.PublicId);
    }

    public async Task LogoutAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        await sessions.RevokeAsync(SessionTokenGenerator.Hash(token), cancellationToken);
    }

    public async Task<CurrentUserResponse?> GetCurrentUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return null;
        }

        return new CurrentUserResponse(user.PublicId, user.CreatedAt, user.Id);
    }

    private string GetDummyHash()
    {
        if (_dummyHash is not null)
        {
            return _dummyHash;
        }

        lock (DummyHashLock)
        {
            return _dummyHash ??= passwordHasher.Hash(
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        }
    }
}

