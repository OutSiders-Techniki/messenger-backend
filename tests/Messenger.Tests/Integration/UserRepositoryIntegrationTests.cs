using Messenger.Application.Exceptions;
using Messenger.Domain.Entities;
using Messenger.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Tests.Integration;

public class UserRepositoryIntegrationTests(PostgresFixture fixture)
    : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task AddAsync_WhenPublicIdTaken_ThrowsAndAllowsRetryWithAnotherId()
    {
        if (!fixture.Available)
        {
            return;
        }

        var takenId = $"TAKEN{Guid.NewGuid():N}"[..16];
        var freeId = $"FREE{Guid.NewGuid():N}"[..16];

        await SeedAsync(takenId);

        try
        {
            var repository = new UserRepository(fixture.Context);

            await Assert.ThrowsAsync<DuplicatePublicIdException>(
                () => repository.AddAsync(CreateUser(takenId)));

            // Репозиторий не должен удерживать упавшую сущность в трекере,
            // иначе следующая вставка упадёт на том же конфликте.
            await repository.AddAsync(CreateUser(freeId));

            var saved = await fixture.Context.Users
                .AsNoTracking()
                .AnyAsync(u => u.PublicId == freeId);

            Assert.True(saved);
        }
        finally
        {
            await CleanupAsync(takenId, freeId);
        }
    }

    private async Task SeedAsync(string publicId)
    {
        fixture.Context.Users.Add(CreateUser(publicId));
        await fixture.Context.SaveChangesAsync();
    }

    private async Task CleanupAsync(params string[] publicIds)
    {
        await fixture.Context.Users
            .Where(u => publicIds.Contains(u.PublicId))
            .ExecuteDeleteAsync();
    }

    private static User CreateUser(string publicId) => new()
    {
        Id = Guid.NewGuid(),
        PublicId = publicId,
        PasswordHash = "argon2id$stub",
        CreatedAt = DateTime.UtcNow,
    };
}
