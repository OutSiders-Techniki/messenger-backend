using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Messenger.Application.Interfaces;
using Messenger.Application.Services;
using Messenger.Domain.Entities;
using Messenger.Infrastructure.Db;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Messenger.Tests.Integration;

/// <summary>HTTP-контракт текущего фронтенда и реальные транзакции PostgreSQL.</summary>
[Collection("Message database")]
public sealed class MessageBatchDeletionTests(MessengerApiFactory factory) : IClassFixture<MessengerApiFactory>
{
    [Fact]
    public async Task Anonymous_CannotUseBatchDeletionEndpoints()
    {
        using var client = factory.CreateClient();
        foreach (var suffix in new[] { "delete-self", "delete-both" })
        {
            using var response = await client.PostAsJsonAsync($"/dialogs/{Guid.NewGuid()}/messages/{suffix}", new { messageIds = new[] { Guid.NewGuid() } });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [SkippableFact]
    public async Task ClearSelf_EmptyObjectDeletesAllOwnCopiesAndLeavesPeerHistory()
    {
        var setup = await SetupAsync();
        await AssertPostAsync(setup.A, setup.DialogId, "delete-self", new { }, HttpStatusCode.NoContent);
        await InDatabaseAsync(async db =>
        {
            var rows = await db.Messages.Where(x => x.DialogId == setup.DialogId).ToListAsync();
            Assert.Equal(setup.Packets.Length, rows.Count);
            Assert.All(rows, row => Assert.Equal(setup.B.Id, row.OwnerId));
            Assert.Equal(setup.Packets.Length, await db.MessageReceipts.CountAsync(x => x.DialogId == setup.DialogId));
            Assert.Equal(setup.Packets.Length, (await db.Dialogs.SingleAsync(x => x.Id == setup.DialogId)).MessageRevision);
        });
        // Повтор очистки не меняет ревизию, а повтор отправки не возвращает удалённые копии.
        await AssertPostAsync(setup.A, setup.DialogId, "delete-self", new { }, HttpStatusCode.NoContent);
        await AssertReplayDoesNotRestoreAsync(setup);
        await InDatabaseAsync(async db => Assert.Equal(setup.Packets.Length,
            (await db.Dialogs.SingleAsync(x => x.Id == setup.DialogId)).MessageRevision));
    }

    [SkippableFact]
    public async Task SelectedSelfDeletion_PreservesUnselectedAndPeerCopies_EmptyArrayIsNoop()
    {
        var setup = await SetupAsync();
        await AssertPostAsync(setup.A, setup.DialogId, "delete-self", new { messageIds = Array.Empty<Guid>() }, HttpStatusCode.NoContent);
        await InDatabaseAsync(async db => Assert.Equal(setup.Packets.Length * 2, await db.Messages.CountAsync(x => x.DialogId == setup.DialogId)));
        var selected = setup.Packets[0].Id;
        var body = new { messageIds = new[] { selected, selected, Guid.NewGuid() } };
        await AssertPostAsync(setup.A, setup.DialogId, "delete-self", body, HttpStatusCode.NoContent);
        await AssertPostAsync(setup.A, setup.DialogId, "delete-self", body, HttpStatusCode.NoContent);
        await InDatabaseAsync(async db =>
        {
            var rows = await db.Messages.Where(x => x.DialogId == setup.DialogId).ToListAsync();
            Assert.Equal(setup.Packets.Length * 2 - 1, rows.Count);
            Assert.DoesNotContain(rows, x => x.OwnerId == setup.A.Id && x.ClientMessageId == selected);
            Assert.Contains(rows, x => x.OwnerId == setup.B.Id && x.ClientMessageId == selected);
            Assert.Equal(1, (await db.Dialogs.SingleAsync(x => x.Id == setup.DialogId)).MessageRevision);
        });
    }

    [SkippableFact]
    public async Task DeleteBoth_RemovesSelectedPairsAndReplaysCannotRestoreThem()
    {
        var setup = await SetupAsync();
        var selected = setup.Packets.Take(2).Select(x => x.Id).ToArray();
        await AssertPostAsync(setup.B, setup.DialogId, "delete-both", new { messageIds = selected }, HttpStatusCode.NoContent);
        await AssertPostAsync(setup.B, setup.DialogId, "delete-both", new { messageIds = selected }, HttpStatusCode.NoContent);
        await InDatabaseAsync(async db =>
        {
            Assert.False(await db.Messages.AnyAsync(x => x.DialogId == setup.DialogId && selected.Contains(x.ClientMessageId)));
            Assert.Equal(2, await db.Messages.CountAsync(x => x.DialogId == setup.DialogId));
            Assert.Equal(2, (await db.Dialogs.SingleAsync(x => x.Id == setup.DialogId)).MessageRevision);
        });
        foreach (var packet in setup.Packets.Take(2))
        {
            using var response = await setup.A.Client.PostAsJsonAsync($"/dialogs/{setup.DialogId}/messages", packet.Wire());
            response.EnsureSuccessStatusCode();
        }
        await InDatabaseAsync(async db => Assert.False(await db.Messages.AnyAsync(x => x.DialogId == setup.DialogId && selected.Contains(x.ClientMessageId))));
    }

    [SkippableFact]
    public async Task InvalidSelectionAndStranger_DoNotDeleteAnything()
    {
        var setup = await SetupAsync();
        foreach (var suffix in new[] { "delete-self", "delete-both" })
        {
            await AssertPostAsync(setup.C, setup.DialogId, suffix, new { messageIds = new[] { setup.Packets[0].Id } }, HttpStatusCode.NotFound);
            await AssertPostAsync(setup.A, setup.DialogId, suffix, new { messageIds = new[] { "not-a-guid" } }, HttpStatusCode.BadRequest);
            await AssertPostAsync(setup.A, setup.DialogId, suffix, new { messageIds = new[] { Guid.Empty } }, HttpStatusCode.BadRequest);
            await AssertPostAsync(setup.A, setup.DialogId, suffix,
                new { messageIds = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToArray() }, HttpStatusCode.BadRequest);
        }
        await AssertPostAsync(setup.C, setup.DialogId, "delete-self", new { }, HttpStatusCode.NotFound);
        await AssertPostAsync(setup.A, setup.DialogId, "delete-both", new { }, HttpStatusCode.BadRequest);
        await AssertPostAsync(setup.A, setup.DialogId, "delete-both", new { messageIds = Array.Empty<Guid>() }, HttpStatusCode.BadRequest);
        await InDatabaseAsync(async db => Assert.Equal(setup.Packets.Length * 2, await db.Messages.CountAsync(x => x.DialogId == setup.DialogId)));
    }

    [SkippableFact]
    public async Task BatchDeletion_PreservesSharedFileUntilLastOwnerAndThenRemovesIt()
    {
        var setup = await SetupAsync();
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAttachmentContentStore>();
        await using var stream = new MemoryStream(new byte[32]);
        var stored = await store.SaveAsync(stream, 1024);
        var path = Path.Combine(scope.ServiceProvider.GetRequiredService<IConfiguration>()["AttachmentStorage:RootPath"]!, stored.StorageKey);
        await InDatabaseAsync(async db =>
        {
            var copies = await db.Messages.Where(x => x.DialogId == setup.DialogId && x.ClientMessageId == setup.Packets[0].Id).ToListAsync();
            foreach (var copy in copies)
                db.Attachments.Add(new Attachment
                {
                    Id = Guid.NewGuid(), MessageId = copy.Id, ClientAttachmentId = Guid.NewGuid(),
                    StorageKey = stored.StorageKey, Size = stored.Size, EncryptedMetadata = [1], UploadedAt = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();
        });
        await AssertPostAsync(setup.A, setup.DialogId, "delete-self", new { }, HttpStatusCode.NoContent);
        Assert.True(File.Exists(path));
        await AssertPostAsync(setup.B, setup.DialogId, "delete-both", new { messageIds = setup.Packets.Select(x => x.Id).ToArray() }, HttpStatusCode.NoContent);
        Assert.False(File.Exists(path));
        await InDatabaseAsync(async db => Assert.False(await db.Attachments.AnyAsync(x => x.StorageKey == stored.StorageKey)));
    }

    [SkippableFact]
    public async Task DeleteBoth_FailureAfterRowsWereDeletedRollsBackEntireBatchAndReceipts()
    {
        var setup = await SetupAsync();
        var interceptor = new FailQueueQuery();
        using var failingFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddScoped(_ => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(MessengerApiFactory.ConnectionString).UseSnakeCaseNamingConvention().AddInterceptors(interceptor).Options))));
        using var client = failingFactory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"messenger_session={setup.A.Token}");
        using var response = await client.PostAsJsonAsync($"/dialogs/{setup.DialogId}/messages/delete-both",
            new { messageIds = setup.Packets.Select(x => x.Id).ToArray() });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, interceptor.Failures);
        await InDatabaseAsync(async db =>
        {
            Assert.Equal(setup.Packets.Length * 2, await db.Messages.CountAsync(x => x.DialogId == setup.DialogId));
            Assert.False(await db.MessageReceipts.AnyAsync(x => x.DialogId == setup.DialogId));
            Assert.Equal(0, (await db.Dialogs.SingleAsync(x => x.Id == setup.DialogId)).MessageRevision);
        });
    }

    private async Task<Setup> SetupAsync()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var available = await db.Database.CanConnectAsync();
            if (!available && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MESSENGER_TEST_DB")))
                Skip.If(true, "PostgreSQL unavailable. Set MESSENGER_TEST_DB to require integration checks.");
            Assert.True(available, "Configured MESSENGER_TEST_DB is unavailable.");
            await db.Database.MigrateAsync();
        }
        var a = await ActorAsync(); var b = await ActorAsync(); var c = await ActorAsync();
        var dialogId = Guid.NewGuid();
        var packets = Enumerable.Range(0, 3).Select(_ => new Packet(Guid.NewGuid(),
            DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(12),
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))).ToArray();
        await InDatabaseAsync(async db =>
        {
            var dialog = new Dialog { Id = dialogId, PairKey = Dialog.PairKeyFor(a.Id, b.Id), CreatedAt = DateTime.UtcNow };
            foreach (var owner in new[] { a.Id, b.Id })
                dialog.Participants.Add(new DialogParticipant { DialogId = dialogId, UserId = owner, JoinedAt = dialog.CreatedAt });
            db.Dialogs.Add(dialog);
            foreach (var packet in packets)
                foreach (var owner in new[] { a.Id, b.Id })
                    db.Messages.Add(new Message
                    {
                        Id = Guid.NewGuid(), DialogId = dialogId, OwnerId = owner, SenderId = a.Id,
                        ClientMessageId = packet.Id, Version = 1, SentAt = packet.SentAt.UtcDateTime,
                        Iv = packet.Iv, Ciphertext = packet.Ct,
                    });
            await db.SaveChangesAsync();
        });
        return new Setup(a, b, c, dialogId, packets);
    }

    private async Task<Actor> ActorAsync()
    {
        var id = Guid.NewGuid();
        var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        await InDatabaseAsync(async db =>
        {
            db.Users.Add(new User { Id = id, PublicId = id.ToString("N")[..12].ToUpperInvariant(), PasswordHash = "integration-test", CreatedAt = DateTime.UtcNow });
            db.Sessions.Add(new Session { Id = Guid.NewGuid(), UserId = id, TokenHash = SessionTokenGenerator.Hash(token), CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddHours(1) });
            await db.SaveChangesAsync();
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"messenger_session={token}");
        return new Actor(id, token, client);
    }

    private async Task AssertReplayDoesNotRestoreAsync(Setup setup)
    {
        foreach (var packet in setup.Packets)
        {
            using var replay = await setup.A.Client.PostAsJsonAsync($"/dialogs/{setup.DialogId}/messages", packet.Wire());
            replay.EnsureSuccessStatusCode();
        }
        await InDatabaseAsync(async db => Assert.False(await db.Messages.AnyAsync(x => x.DialogId == setup.DialogId && x.OwnerId == setup.A.Id)));
    }

    private static async Task AssertPostAsync(Actor actor, Guid dialogId, string suffix, object body, HttpStatusCode expected)
    {
        using var response = await actor.Client.PostAsJsonAsync($"/dialogs/{dialogId}/messages/{suffix}", body);
        Assert.Equal(expected, response.StatusCode);
    }

    private async Task InDatabaseAsync(Func<AppDbContext, Task> action)
    {
        using var scope = factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private sealed class FailQueueQuery : DbCommandInterceptor
    {
        public int Failures { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.Transaction is not null && command.CommandText.Contains("pending_file_deletions", StringComparison.Ordinal))
            {
                Failures++;
                throw new InvalidOperationException("Injected failure after message deletion, before transaction commit.");
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed record Actor(Guid Id, string Token, HttpClient Client);
    private sealed record Setup(Actor A, Actor B, Actor C, Guid DialogId, Packet[] Packets);
    private sealed record Packet(Guid Id, DateTimeOffset SentAt, byte[] Iv, byte[] Ct)
    {
        public object Wire() => new { v = 1, id = Id, sentAt = SentAt.ToUnixTimeMilliseconds(), iv = Convert.ToBase64String(Iv), ct = Convert.ToBase64String(Ct) };
    }
}
