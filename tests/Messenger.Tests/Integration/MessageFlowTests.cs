using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Messenger.Application.Services;
using Messenger.Application.Interfaces;
using Messenger.Domain.Entities;
using Messenger.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Messenger.Tests.Integration;

/// <summary>Real HTTP, authentication, PostgreSQL transactions and SignalR; no message repository mocks.</summary>
[Collection("Message database")]
public class MessageFlowTests(MessengerApiFactory factory) : IClassFixture<MessengerApiFactory>
{
    [Fact]
    public async Task Anonymous_CannotReadSendDeleteSyncOrNegotiate()
    {
        using var client = factory.CreateClient();
        var path = $"/dialogs/{Guid.NewGuid()}/messages";
        using var read = await client.GetAsync(path);
        using var send = await client.PostAsJsonAsync(path, Packet());
        using var delete = await client.DeleteAsync($"{path}/{Guid.NewGuid()}");
        using var sync = await client.PostAsJsonAsync($"{path}/sync", new { ids = Array.Empty<Guid>() });
        using var negotiate = await client.PostAsync("/hubs/messages/negotiate?negotiateVersion=1", null);
        foreach (var response in new[] { read, send, delete, sync, negotiate })
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SkippableFact]
    public async Task Sending_StoresExactlyTwoCopies_AndNeverExposesThemToStrangers()
    {
        var setup = await SetupAsync();
        var packet = Packet();
        var response = await SendAsync(setup.A, setup.DialogId, packet);
        Assert.True(response.GetProperty("created").GetBoolean());
        await InDatabaseAsync(async db =>
        {
            var rows = await db.Messages.Where(m => m.DialogId == setup.DialogId).ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.Equal(new[] { setup.A.Id, setup.B.Id }.Order(), rows.Select(m => m.OwnerId).Order());
            Assert.All(rows, m => Assert.Equal(setup.A.Id, m.SenderId));
        });
        foreach (var actor in new[] { setup.A, setup.B })
        {
            var items = (await HistoryAsync(actor, setup.DialogId)).GetProperty("items");
            Assert.Single(items.EnumerateArray());
            Assert.Equal(packet.Id, items[0].GetProperty("id").GetGuid());
            Assert.Equal(packet.Ct, items[0].GetProperty("ct").GetBytesFromBase64());
        }
        var path = $"/dialogs/{setup.DialogId}/messages";
        foreach (var responseToStranger in new[]
                 {
                     await setup.C.Client.GetAsync(path),
                     await setup.C.Client.PostAsJsonAsync(path, packet),
                     await setup.C.Client.DeleteAsync($"{path}/{packet.Id}?forEveryone=true"),
                     await setup.C.Client.PostAsJsonAsync($"{path}/sync", new { ids = new[] { packet.Id } }),
                 })
        {
            using (responseToStranger)
                Assert.Equal(HttpStatusCode.NotFound, responseToStranger.StatusCode);
        }
    }

    [SkippableFact]
    public async Task ConcurrentRetries_CreateOnlyOnePair_AndChangedPayloadConflicts()
    {
        var setup = await SetupAsync();
        var packet = Packet();
        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => SendAsync(setup.A, setup.DialogId, packet)));
        Assert.Single(results, r => r.GetProperty("created").GetBoolean());
        await InDatabaseAsync(async db => Assert.Equal(2,
            await db.Messages.CountAsync(m => m.DialogId == setup.DialogId)));
        using var changed = await setup.A.Client.PostAsJsonAsync(
            $"/dialogs/{setup.DialogId}/messages", packet with { Ct = new byte[17] });
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var stolenId = await setup.B.Client.PostAsJsonAsync(
            $"/dialogs/{setup.DialogId}/messages", packet);
        Assert.Equal(HttpStatusCode.Conflict, stolenId.StatusCode);
    }

    [SkippableFact]
    public async Task DeleteOwnCopy_LeavesCompanionCopy_AndRetryDoesNotResurrect()
    {
        var setup = await SetupAsync();
        var packet = Packet();
        await SendAsync(setup.A, setup.DialogId, packet);
        using var deleted = await setup.A.Client.DeleteAsync($"/dialogs/{setup.DialogId}/messages/{packet.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await InDatabaseAsync(async db =>
        {
            var row = Assert.Single(await db.Messages.Where(m => m.DialogId == setup.DialogId).ToListAsync());
            Assert.Equal(setup.B.Id, row.OwnerId);
        });
        await SendAsync(setup.A, setup.DialogId, packet);
        Assert.Empty((await HistoryAsync(setup.A, setup.DialogId)).GetProperty("items").EnumerateArray());
        Assert.Single((await HistoryAsync(setup.B, setup.DialogId)).GetProperty("items").EnumerateArray());
    }

    [SkippableFact]
    public async Task RecipientMayDeleteForEveryone_AndOfflineSyncReportsDeletion()
    {
        var setup = await SetupAsync();
        var packet = Packet();
        await SendAsync(setup.A, setup.DialogId, packet);
        using var deleted = await setup.B.Client.DeleteAsync(
            $"/dialogs/{setup.DialogId}/messages/{packet.Id}?forEveryone=true");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await InDatabaseAsync(async db => Assert.False(
            await db.Messages.AnyAsync(m => m.DialogId == setup.DialogId)));
        foreach (var actor in new[] { setup.A, setup.B })
        {
            using var sync = await actor.Client.PostAsJsonAsync(
                $"/dialogs/{setup.DialogId}/messages/sync", new { ids = new[] { packet.Id } });
            sync.EnsureSuccessStatusCode();
            var body = await JsonAsync(sync);
            Assert.Contains(packet.Id, body.GetProperty("missingIds").EnumerateArray().Select(x => x.GetGuid()));
            Assert.True(body.GetProperty("revision").GetInt64() >= 2);
        }
        await SendAsync(setup.A, setup.DialogId, packet);
        await InDatabaseAsync(async db => Assert.False(
            await db.Messages.AnyAsync(m => m.DialogId == setup.DialogId)));
    }

    [SkippableFact]
    public async Task SendRetryRacingDeletion_CannotRecreateCopies()
    {
        var setup = await SetupAsync();
        var packet = Packet();
        await SendAsync(setup.A, setup.DialogId, packet);
        await Task.WhenAll(
            SendAsync(setup.A, setup.DialogId, packet),
            DeleteForEveryoneAsync(setup.B, setup.DialogId, packet.Id));
        await InDatabaseAsync(async db => Assert.False(
            await db.Messages.AnyAsync(m => m.DialogId == setup.DialogId)));
    }

    [SkippableFact]
    public async Task History_KeysetPaginationHasNoDuplicatesAtIdenticalTimestamps()
    {
        var setup = await SetupAsync();
        var sentAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var ids = new HashSet<Guid>();
        for (var i = 0; i < 7; i++)
        {
            var packet = Packet() with { SentAt = DateTimeOffset.FromUnixTimeMilliseconds(sentAt) };
            ids.Add(packet.Id);
            await SendAsync(setup.A, setup.DialogId, packet);
        }
        var found = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = await HistoryAsync(setup.B, setup.DialogId, 2, cursor);
            found.AddRange(page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()));
            cursor = page.GetProperty("nextCursor").GetString();
            Assert.True(found.Count <= 7, "Cursor repeated a page.");
        } while (cursor is not null);
        Assert.Equal(7, found.Count);
        Assert.True(ids.SetEquals(found));
    }

    [SkippableFact]
    public async Task InvalidPackets_AreRejectedWithoutPersistingAnything()
    {
        var setup = await SetupAsync();
        var valid = Packet();
        foreach (var invalid in new[]
                 {
                     valid with { V = 2 }, valid with { Id = Guid.Empty },
                     valid with { Iv = new byte[11] }, valid with { Iv = null! },
                     valid with { Ct = new byte[15] }, valid with { Ct = null! },
                     valid with { Ct = new byte[65537] },
                     valid with { SentAt = default },
                 })
        {
            using var response = await setup.A.Client.PostAsJsonAsync(
                $"/dialogs/{setup.DialogId}/messages", invalid);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var oversizedSync = await setup.A.Client.PostAsJsonAsync(
            $"/dialogs/{setup.DialogId}/messages/sync", new { ids = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()) });
        Assert.Equal(HttpStatusCode.BadRequest, oversizedSync.StatusCode);
        using var badCursor = await setup.A.Client.GetAsync($"/dialogs/{setup.DialogId}/messages?before=invalid");
        Assert.Equal(HttpStatusCode.BadRequest, badCursor.StatusCode);
        await InDatabaseAsync(async db => Assert.False(await db.Messages.AnyAsync(m => m.DialogId == setup.DialogId)));
    }

    [SkippableFact]
    public async Task Realtime_DeliversOnlyToParticipants_AndReportsDeletion()
    {
        var setup = await SetupAsync();
        using var recipient = await ConnectAsync(setup.B);
        using var outsider = await ConnectAsync(setup.C);
        var packet = Packet();
        await SendAsync(setup.A, setup.DialogId, packet);
        var received = await ReceiveEventAsync(recipient, "MessageReceived");
        Assert.Equal(packet.Id, received.GetProperty("arguments")[0].GetProperty("message").GetProperty("id").GetGuid());
        await DeleteForEveryoneAsync(setup.A, setup.DialogId, packet.Id);
        var deletion = await ReceiveEventAsync(recipient, "MessageDeleted");
        Assert.Equal(packet.Id, deletion.GetProperty("arguments")[0].GetProperty("id").GetGuid());
        // No broadcast to a third authenticated account, even though its connection is active.
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await ReceiveEventAsync(outsider, "MessageReceived", deadline.Token));
    }

    [SkippableFact]
    public async Task Realtime_RevokedSessionDoesNotReceiveNewMessages()
    {
        var setup = await SetupAsync();
        using var recipient = await ConnectAsync(setup.B);
        using var logout = await setup.B.Client.PostAsync("/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        await SendAsync(setup.A, setup.DialogId, Packet());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await ReceiveEventAsync(recipient, "MessageReceived", deadline.Token);
            Assert.Fail("A revoked session received ciphertext.");
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or EndOfStreamException) { }
    }

    [SkippableFact]
    public async Task Deletion_RemovesAttachmentRowsAndFileOnlyAfterLastOwnerCopy()
    {
        var setup = await SetupAsync();
        var packet = Packet();
        await SendAsync(setup.A, setup.DialogId, packet);
        using var scope = factory.Services.CreateScope();
        var content = scope.ServiceProvider.GetRequiredService<IAttachmentContentStore>();
        await using var bytes = new MemoryStream(new byte[32]);
        var stored = await content.SaveAsync(bytes, 1024);
        var root = scope.ServiceProvider.GetRequiredService<IConfiguration>()["AttachmentStorage:RootPath"]!;
        var path = Path.Combine(root, stored.StorageKey);
        await InDatabaseAsync(async db =>
        {
            var copies = await db.Messages.Where(m => m.DialogId == setup.DialogId).ToListAsync();
            foreach (var copy in copies)
                db.Attachments.Add(new Attachment
                {
                    Id = Guid.NewGuid(), MessageId = copy.Id, ClientAttachmentId = Guid.NewGuid(),
                    StorageKey = stored.StorageKey, Size = stored.Size, EncryptedMetadata = [1], UploadedAt = DateTime.UtcNow,
                });
            await db.SaveChangesAsync();
        });
        using var deleteOwn = await setup.A.Client.DeleteAsync($"/dialogs/{setup.DialogId}/messages/{packet.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteOwn.StatusCode);
        Assert.True(File.Exists(path));
        await InDatabaseAsync(async db => Assert.Equal(1, await db.Attachments.CountAsync(a => a.StorageKey == stored.StorageKey)));
        await DeleteForEveryoneAsync(setup.B, setup.DialogId, packet.Id);
        Assert.False(File.Exists(path));
        await InDatabaseAsync(async db => Assert.False(await db.Attachments.AnyAsync(a => a.StorageKey == stored.StorageKey)));
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
            // Schema/migration failures always fail; they are never treated as missing infrastructure.
            await db.Database.MigrateAsync();
        }
        var a = await CreateActorAsync();
        var b = await CreateActorAsync();
        var c = await CreateActorAsync();
        using var response = await a.Client.PostAsJsonAsync("/dialogs", new { publicId = b.PublicId });
        response.EnsureSuccessStatusCode();
        return new Setup(a, b, c, (await JsonAsync(response)).GetProperty("id").GetGuid());
    }

    private async Task<Actor> CreateActorAsync()
    {
        var id = Guid.NewGuid();
        var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var publicId = id.ToString("N")[..12].ToUpperInvariant();
        await InDatabaseAsync(async db =>
        {
            db.Users.Add(new User { Id = id, PublicId = publicId, PasswordHash = "integration-test", CreatedAt = DateTime.UtcNow });
            db.Sessions.Add(new Session
            {
                Id = Guid.NewGuid(), UserId = id, TokenHash = SessionTokenGenerator.Hash(token),
                CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddHours(1),
            });
            await db.SaveChangesAsync();
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"messenger_session={token}");
        return new Actor(id, publicId, token, client);
    }

    private async Task InDatabaseAsync(Func<AppDbContext, Task> action)
    {
        using var scope = factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static PacketBody Packet() => new(
        1, Guid.NewGuid(), DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(12),
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    private static async Task<JsonElement> SendAsync(Actor actor, Guid dialogId, PacketBody packet)
    {
        using var response = await actor.Client.PostAsJsonAsync($"/dialogs/{dialogId}/messages", packet);
        response.EnsureSuccessStatusCode();
        return await JsonAsync(response);
    }

    private static async Task DeleteForEveryoneAsync(Actor actor, Guid dialogId, Guid id)
    {
        using var response = await actor.Client.DeleteAsync($"/dialogs/{dialogId}/messages/{id}?forEveryone=true");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<JsonElement> HistoryAsync(Actor actor, Guid dialogId, int limit = 50, string? cursor = null)
    {
        using var response = await actor.Client.GetAsync($"/dialogs/{dialogId}/messages?limit={limit}"
            + (cursor is null ? "" : $"&before={Uri.EscapeDataString(cursor)}"));
        response.EnsureSuccessStatusCode();
        return await JsonAsync(response);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<WebSocket> ConnectAsync(Actor actor)
    {
        var client = factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers["Cookie"] = $"messenger_session={actor.Token}";
        var socket = await client.ConnectAsync(new Uri("ws://localhost/hubs/messages"), CancellationToken.None);
        await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e")),
            WebSocketMessageType.Text, true, CancellationToken.None);
        var buffer = new byte[4096];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
        Assert.Contains("{}", Encoding.UTF8.GetString(buffer, 0, result.Count));
        // Handshake is acknowledged before OnConnectedAsync necessarily finishes.
        // A completed invocation guarantees registration before the first event assertion.
        await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(
            "{\"type\":1,\"invocationId\":\"ready\",\"target\":\"Ping\",\"arguments\":[]}\u001e")),
            WebSocketMessageType.Text, true, timeout.Token);
        var ready = false;
        while (!ready)
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
            Assert.NotEqual(WebSocketMessageType.Close, result.MessageType);
            foreach (var json in Encoding.UTF8.GetString(buffer, 0, result.Count).Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
            {
                using var completion = JsonDocument.Parse(json);
                if (completion.RootElement.TryGetProperty("invocationId", out var invocation) && invocation.GetString() == "ready")
                {
                    Assert.False(completion.RootElement.TryGetProperty("error", out _));
                    ready = true;
                }
            }
        }
        return socket;
    }

    private static async Task<JsonElement> ReceiveEventAsync(WebSocket socket, string target, CancellationToken token = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (!token.CanBeCanceled) deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var pending = new StringBuilder();
        var buffer = new byte[128 * 1024];
        while (true)
        {
            var frame = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), deadline.Token);
            if (frame.MessageType == WebSocketMessageType.Close) throw new EndOfStreamException();
            pending.Append(Encoding.UTF8.GetString(buffer, 0, frame.Count));
            while (pending.ToString().IndexOf('\u001e') is var end && end >= 0)
            {
                var json = pending.ToString(0, end);
                pending.Remove(0, end + 1);
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("target", out var name) && name.GetString() == target)
                    return document.RootElement.Clone();
            }
        }
    }

    private sealed record Actor(Guid Id, string PublicId, string Token, HttpClient Client);
    private sealed record Setup(Actor A, Actor B, Actor C, Guid DialogId);
    private sealed record PacketBody(int V, Guid Id, DateTimeOffset SentAt, byte[] Iv, byte[] Ct);
}

[CollectionDefinition("Message database", DisableParallelization = true)]
public class MessageDatabaseCollection;
