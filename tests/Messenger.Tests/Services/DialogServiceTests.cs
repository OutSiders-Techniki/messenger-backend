using Messenger.Application.DTOs;
using Messenger.Application.Exceptions;
using Messenger.Application.Interfaces;
using Messenger.Application.Services;
using Messenger.Domain.Entities;

namespace Messenger.Tests.Services;

public class DialogServiceTests
{
    private readonly FakeDialogRepository _dialogs;
    private readonly FakeUserRepository _users = new();

    public DialogServiceTests()
    {
        _dialogs = new FakeDialogRepository(_users);
    }

    private DialogService CreateSut() => new(_users, _dialogs);

    [Fact]
    public async Task CreateAsync_WithNewCompanion_CreatesDialogWithTwoParticipants()
    {
        var creator = SeedUser("CREATOR");
        var companion = SeedUser("COMPANION");
        var sut = CreateSut();

        var result = await sut.CreateAsync(creator.Id, "COMPANION");

        Assert.True(result.Created);

        var dialog = Assert.Single(_dialogs.Dialogs);
        Assert.Equal(
            new[] { creator.Id, companion.Id }.Order(),
            dialog.Participants.Select(p => p.UserId).Order());
    }

    [Fact]
    public async Task CreateAsync_WhenDialogExists_ReturnsSameDialogAndDoesNotCreateNew()
    {
        var creator = SeedUser("CREATOR");
        SeedUser("COMPANION");
        var sut = CreateSut();

        var first = await sut.CreateAsync(creator.Id, "COMPANION");
        var second = await sut.CreateAsync(creator.Id, "COMPANION");

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Dialog.Id, second.Dialog.Id);
        Assert.Single(_dialogs.Dialogs);
    }

    [Fact]
    public async Task CreateAsync_FromOtherSide_ReturnsSameDialog()
    {
        var creator = SeedUser("CREATOR");
        var companion = SeedUser("COMPANION");
        var sut = CreateSut();

        var first = await sut.CreateAsync(creator.Id, "COMPANION");
        var second = await sut.CreateAsync(companion.Id, "CREATOR");

        Assert.False(second.Created);
        Assert.Equal(first.Dialog.Id, second.Dialog.Id);
        Assert.Single(_dialogs.Dialogs);
    }

    [Fact]
    public async Task CreateAsync_WithSelf_ThrowsValidation()
    {
        var creator = SeedUser("CREATOR");
        var sut = CreateSut();

        await Assert.ThrowsAsync<ValidationException>(() => sut.CreateAsync(creator.Id, "CREATOR"));

        Assert.Empty(_dialogs.Dialogs);
    }

    [Fact]
    public async Task CreateAsync_WithUnknownCompanion_ThrowsNotFound()
    {
        var creator = SeedUser("CREATOR");
        var sut = CreateSut();

        await Assert.ThrowsAsync<UserNotFoundException>(() => sut.CreateAsync(creator.Id, "NOPE"));

        Assert.Empty(_dialogs.Dialogs);
    }

    [Fact]
    public async Task GetListAsync_ReturnsOnlyOwnDialogsWithCompanion()
    {
        var creator = SeedUser("CREATOR");
        var companion = SeedUser("COMPANION");
        var stranger = SeedUser("STRANGER");
        var otherStranger = SeedUser("OTHER");

        var sut = CreateSut();

        await sut.CreateAsync(creator.Id, "COMPANION");
        await sut.CreateAsync(stranger.Id, "OTHER");

        var list = await sut.GetListAsync(creator.Id);

        var item = Assert.Single(list);
        Assert.Equal(companion.PublicId, item.CompanionPublicId);
    }

    [Fact]
    public async Task GetListAsync_ForUserWithoutDialogs_ReturnsEmpty()
    {
        SeedUser("CREATOR");
        var sut = CreateSut();

        var list = await sut.GetListAsync(Guid.NewGuid());

        Assert.Empty(list);
    }

    [Fact]
    public void PairKeyFor_IsSymmetric()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        Assert.Equal(Dialog.PairKeyFor(first, second), Dialog.PairKeyFor(second, first));
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

    private sealed class FakeDialogRepository(FakeUserRepository users) : IDialogRepository
    {
        public List<Dialog> Dialogs { get; } = [];

        public Task<DialogLookup> GetOrCreateAsync(
            Guid firstUserId,
            Guid secondUserId,
            CancellationToken cancellationToken = default)
        {
            var pairKey = Dialog.PairKeyFor(firstUserId, secondUserId);

            var existing = Dialogs.SingleOrDefault(d => d.PairKey == pairKey);

            if (existing is not null)
            {
                return Task.FromResult(new DialogLookup(existing, Created: false));
            }

            var dialog = new Dialog
            {
                Id = Guid.NewGuid(),
                PairKey = pairKey,
                CreatedAt = DateTime.UtcNow,
            };

            dialog.Participants.Add(new DialogParticipant
            {
                DialogId = dialog.Id,
                UserId = firstUserId,
                JoinedAt = dialog.CreatedAt,
                User = users.Users.Single(u => u.Id == firstUserId),
            });

            dialog.Participants.Add(new DialogParticipant
            {
                DialogId = dialog.Id,
                UserId = secondUserId,
                JoinedAt = dialog.CreatedAt,
                User = users.Users.Single(u => u.Id == secondUserId),
            });

            Dialogs.Add(dialog);

            return Task.FromResult(new DialogLookup(dialog, Created: true));
        }

        public Task<IReadOnlyList<DialogListItem>> GetForUserAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<DialogListItem> result = Dialogs
                .Where(d => d.Participants.Any(p => p.UserId == userId))
                .Select(d => new DialogListItem(
                    d.Id,
                    d.Participants.First(p => p.UserId != userId).User.PublicId,
                    d.CreatedAt))
                .ToList();

            return Task.FromResult(result);
        }
    }
}