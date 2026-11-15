using Messenger.Application.DTOs;
using Messenger.Application.Exceptions;
using Messenger.Application.Interfaces;
using Messenger.Application.Services;
using Messenger.Domain.Entities;

namespace Messenger.Tests.Services;

public class RegistrationServiceTests
{
    private readonly FakeUserRepository _users = new();
    private readonly FakePublicIdGenerator _idGenerator = new();
    private readonly FakePasswordHasher _hasher = new();

    private RegistrationService CreateSut() => new(_users, _idGenerator, _hasher);

    [Fact]
    public async Task RegisterAsync_StoresHashedPasswordNotPlain()
    {
        var sut = CreateSut();

        await sut.RegisterAsync(new RegisterRequest { Password = "password123" });

        var user = Assert.Single(_users.Users);
        Assert.NotEqual("password123", user.PasswordHash);
        Assert.Equal("hashed:password123", user.PasswordHash);
    }

    [Fact]
    public async Task RegisterAsync_ReturnsGeneratedPublicId()
    {
        var sut = CreateSut();

        var response = await sut.RegisterAsync(new RegisterRequest { Password = "password123" });

        Assert.Equal(_idGenerator.LastGenerated, response.PublicId);
        Assert.Equal(response.PublicId, Assert.Single(_users.Users).PublicId);
    }

    [Fact]
    public async Task RegisterAsync_RetriesWithNewIdOnConflict()
    {
        _idGenerator.Queue = ["DUP", "DUP", "FREE"];
        _users.TakenIds.Add("DUP");
        var sut = CreateSut();

        var response = await sut.RegisterAsync(new RegisterRequest { Password = "password123" });

        Assert.Equal("FREE", response.PublicId);
        Assert.Single(_users.Users);
        Assert.Equal(3, _idGenerator.Calls);
    }

    [Fact]
    public async Task RegisterAsync_ThrowsAfterMaxIdAttempts()
    {
        _idGenerator.Queue = ["DUP", "DUP", "DUP", "DUP", "DUP", "DUP"];
        _users.TakenIds.Add("DUP");
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.RegisterAsync(new RegisterRequest { Password = "password123" }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("short")]
    public async Task RegisterAsync_RejectsInvalidPassword(string password)
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ValidationException>(
            () => sut.RegisterAsync(new RegisterRequest { Password = password }));

        Assert.Empty(_users.Users);
    }

    [Fact]
    public async Task RegisterAsync_RejectsTooLongPassword()
    {
        var sut = CreateSut();
        var password = new string('a', RegistrationService.MaxPasswordLength + 1);

        await Assert.ThrowsAsync<ValidationException>(
            () => sut.RegisterAsync(new RegisterRequest { Password = password }));
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        /// <summary>ID, которые уже заняты другими пользователями.</summary>
        public HashSet<string> TakenIds { get; } = [];

        public List<User> Users { get; } = [];

        public Task<User?> GetByPublicIdAsync(string publicId, CancellationToken cancellationToken = default)
            => Task.FromResult(Users.SingleOrDefault(u => u.PublicId == publicId));

        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Users.SingleOrDefault(u => u.Id == id));

        public Task AddAsync(User user, CancellationToken cancellationToken = default)
        {
            if (TakenIds.Contains(user.PublicId) || Users.Any(u => u.PublicId == user.PublicId))
            {
                throw new DuplicatePublicIdException(user.PublicId);
            }

            Users.Add(user);
            return Task.CompletedTask;
        }
    }

    private sealed class FakePublicIdGenerator : IPublicIdGenerator
    {
        public List<string> Queue { get; set; } = [];

        public string LastGenerated { get; private set; } = string.Empty;

        public int Calls { get; private set; }

        public string Generate()
        {
            Calls++;

            if (Calls <= Queue.Count)
            {
                LastGenerated = Queue[Calls - 1];
            }
            else
            {
                LastGenerated = $"ID{Calls:D6}";
            }

            return LastGenerated;
        }
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => $"hashed:{password}";

        public bool Verify(string password, string encodedHash) => Hash(password) == encodedHash;
    }
}
