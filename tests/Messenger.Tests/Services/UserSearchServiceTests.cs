using Messenger.Application.Exceptions;
using Messenger.Application.Interfaces;
using Messenger.Application.Services;
using Messenger.Domain.Entities;

namespace Messenger.Tests.Services;

public class UserSearchServiceTests
{
    private readonly FakeUserRepository _users = new();

    private UserSearchService CreateSut() => new(_users);

    [Fact]
    public async Task FindByPublicIdAsync_WithExactId_ReturnsUser()
    {
        var user = SeedUser("MBY575N7SDW7");
        var sut = CreateSut();

        var result = await sut.FindByPublicIdAsync("MBY575N7SDW7");

        Assert.Equal(user.PublicId, result.PublicId);
        Assert.Equal(user.CreatedAt, result.CreatedAt);
    }

    [Fact]
    public async Task FindByPublicIdAsync_WithPartialId_ThrowsNotFound()
    {
        SeedUser("MBY575N7SDW7");
        var sut = CreateSut();

        await Assert.ThrowsAsync<UserNotFoundException>(
            () => sut.FindByPublicIdAsync("MBY575"));
    }

    [Fact]
    public async Task FindByPublicIdAsync_WithDifferentCase_ThrowsNotFound()
    {
        SeedUser("MBY575N7SDW7");
        var sut = CreateSut();

        await Assert.ThrowsAsync<UserNotFoundException>(
            () => sut.FindByPublicIdAsync("mby575n7sdw7"));
    }

    [Fact]
    public async Task FindByPublicIdAsync_WithUnknownId_ThrowsNotFound()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<UserNotFoundException>(
            () => sut.FindByPublicIdAsync("NOPE"));
    }

    [Fact]
    public async Task FindByPublicIdAsync_PassesFullIdToRepository()
    {
        SeedUser("MBY575N7SDW7");
        var sut = CreateSut();

        await sut.FindByPublicIdAsync("MBY575N7SDW7");

        // Поиск идёт по полному ID: частичное совпадение не ищем.
        Assert.Equal("MBY575N7SDW7", _users.LastQueriedPublicId);
    }

    private User SeedUser(string publicId)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            PublicId = publicId,
            PasswordHash = "hash",
            CreatedAt = DateTime.UtcNow,
        };

        _users.Users.Add(user);

        return user;
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        public List<User> Users { get; } = [];

        public string? LastQueriedPublicId { get; private set; }

        public Task<User?> GetByPublicIdAsync(string publicId, CancellationToken cancellationToken = default)
        {
            LastQueriedPublicId = publicId;

            return Task.FromResult(Users.SingleOrDefault(u => u.PublicId == publicId));
        }

        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Users.SingleOrDefault(u => u.Id == id));

        public Task AddAsync(User user, CancellationToken cancellationToken = default)
        {
            Users.Add(user);

            return Task.CompletedTask;
        }
    }
}
