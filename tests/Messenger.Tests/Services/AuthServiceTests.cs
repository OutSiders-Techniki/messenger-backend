using Messenger.Application.DTOs;
using Messenger.Application.Exceptions;
using Messenger.Application.Interfaces;
using Messenger.Application.Options;
using Messenger.Application.Services;
using Messenger.Domain.Entities;
using Microsoft.Extensions.Options;

namespace Messenger.Tests.Services;

public class AuthServiceTests
{
    private readonly FakeUserRepository _users = new();
    private readonly FakeSessionStore _sessions = new();
    private readonly FakeSessionTokenGenerator _tokens = new();
    private readonly FakePasswordHasher _hasher = new();

    private AuthService CreateSut() => new(
        _users,
        _sessions,
        _tokens,
        _hasher,
        Options.Create(new SessionCookieOptions { LifetimeHours = 1 }));

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsTokenAndCreatesSession()
    {
        var user = SeedUser("ABC123", "secret-password");
        var sut = CreateSut();

        var (token, publicId) = await sut.LoginAsync(new LoginRequest { PublicId = "ABC123", Password = "secret-password" });

        Assert.Equal("ABC123", publicId);
        Assert.NotEmpty(token);

        var session = Assert.Single(_sessions.Sessions);
        Assert.Equal(user.Id, session.UserId);
        Assert.Equal(_tokens.LastHash, session.TokenHash);
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_Throws()
    {
        SeedUser("ABC123", "secret-password");
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => sut.LoginAsync(new LoginRequest { PublicId = "ABC123", Password = "wrong-password" }));

        Assert.Empty(_sessions.Sessions);
    }

    [Fact]
    public async Task LoginAsync_WithUnknownPublicId_ThrowsSameException()
    {
        var sut = CreateSut();

        // Одинаковая ошибка для «нет пользователя» и «неверный пароль»:
        // иначе по ответу можно перебором выяснить, какие ID существуют.
        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => sut.LoginAsync(new LoginRequest { PublicId = "NOPE", Password = "secret-password" }));
    }

    [Fact]
    public async Task LoginAsync_StoresOnlyTokenHashNotToken()
    {
        SeedUser("ABC123", "secret-password");
        var sut = CreateSut();

        var (token, _) = await sut.LoginAsync(new LoginRequest { PublicId = "ABC123", Password = "secret-password" });

        var session = Assert.Single(_sessions.Sessions);
        Assert.DoesNotContain(token, session.TokenHash);
    }

    [Fact]
    public async Task LogoutAsync_RevokesSession()
    {
        var sut = CreateSut();
        _sessions.Sessions.Add(new Session
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TokenHash = SessionTokenGenerator.Hash("token-value"),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
        });

        await sut.LogoutAsync("token-value");

        Assert.Empty(_sessions.Sessions);
    }

    [Fact]
    public async Task GetCurrentUserAsync_WithKnownUser_ReturnsUser()
    {
        var user = SeedUser("ABC123", "secret-password");
        var sut = CreateSut();

        var current = await sut.GetCurrentUserAsync(user.Id);

        Assert.NotNull(current);
        Assert.Equal(user.PublicId, current.PublicId);
    }

    [Fact]
    public async Task GetCurrentUserAsync_WithUnknownUser_ReturnsNull()
    {
        var sut = CreateSut();

        var current = await sut.GetCurrentUserAsync(Guid.NewGuid());

        Assert.Null(current);
    }

    [Fact]
    public async Task LogoutAsync_AfterLogin_SessionIsRevoked()
    {
        SeedUser("ABC123", "secret-password");
        var sut = CreateSut();

        var (token, _) = await sut.LoginAsync(new LoginRequest { PublicId = "ABC123", Password = "secret-password" });
        await sut.LogoutAsync(token);

        Assert.Empty(_sessions.Sessions);
    }

    private User SeedUser(string publicId, string password)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            PublicId = publicId,
            PasswordHash = _hasher.Hash(password),
            CreatedAt = DateTime.UtcNow,
        };

        _users.Users.Add(user);

        return user;
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        public List<User> Users { get; } = [];

        public Task<User?> GetByPublicIdAsync(string publicId, CancellationToken cancellationToken = default)
            => Task.FromResult(Users.SingleOrDefault(u => u.PublicId == publicId));

        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Users.SingleOrDefault(u => u.Id == id));

        public Task AddAsync(User user, CancellationToken cancellationToken = default)
        {
            Users.Add(user);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSessionStore : ISessionStore
    {
        public List<Session> Sessions { get; } = [];

        public Task<Session> CreateAsync(
            Guid userId,
            string tokenHash,
            DateTime expiresAt,
            CancellationToken cancellationToken = default)
        {
            var session = new Session
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TokenHash = tokenHash,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt,
            };

            Sessions.Add(session);

            return Task.FromResult(session);
        }

        public Task<Session?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
            => Task.FromResult(Sessions.SingleOrDefault(s => s.TokenHash == tokenHash));

        public Task<bool> RevokeAsync(string tokenHash, CancellationToken cancellationToken = default)
        {
            var session = Sessions.SingleOrDefault(s => s.TokenHash == tokenHash);

            if (session is null)
            {
                return Task.FromResult(false);
            }

            Sessions.Remove(session);

            return Task.FromResult(true);
        }
    }

    private sealed class FakeSessionTokenGenerator : ISessionTokenGenerator
    {
        public string LastHash { get; private set; } = string.Empty;

        public SessionToken Create()
        {
            var token = $"token-{Guid.NewGuid():N}";

            LastHash = SessionTokenGenerator.Hash(token);

            return new SessionToken(token, LastHash);
        }
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => $"hashed:{password}";

        public bool Verify(string password, string encodedHash) => Hash(password) == encodedHash;
    }
}
