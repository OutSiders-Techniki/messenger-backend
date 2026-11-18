using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Messenger.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Messenger.Tests.Integration;

/// <summary>Зашифрованные подписи контактов: доступ, изоляция владельца, формат и удаление.</summary>
public class DialogLabelTests(MessengerApiFactory factory) : IClassFixture<MessengerApiFactory>
{
    private const string Password = "correct horse battery staple";
    private static readonly byte[] Iv = new byte[12];
    private static readonly byte[] Ciphertext = new byte[64];

    [SkippableFact]
    public async Task Label_VisibleOnlyToOwner()
    {
        await EnsureDatabaseAsync();
        var (owner, peer, _, dialogId) = await SetupAsync();

        using var put = await owner.Client.PutAsJsonAsync($"/dialogs/{dialogId}/label",
            new { v = 1, iv = Iv, ct = Ciphertext });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var own = await GetDialogAsync(owner.Client, dialogId);
        Assert.Equal(JsonValueKind.Object, own.GetProperty("label").ValueKind);
        Assert.Equal(1, own.GetProperty("label").GetProperty("v").GetInt32());
        Assert.Equal(64, Base64UrlLength(own.GetProperty("label").GetProperty("ct").GetString()));
        Assert.NotEqual(DateTime.MinValue, own.GetProperty("label").GetProperty("updatedAt").GetDateTime());

        // Собеседник получает label = null: подпись видна только владельцу.
        var peerView = await GetDialogAsync(peer.Client, dialogId);
        Assert.Equal(JsonValueKind.Null, peerView.GetProperty("label").ValueKind);
        var peerList = (await peer.Client.GetFromJsonAsync<JsonElement>("/dialogs"))
            .EnumerateArray().Single();
        Assert.Equal(JsonValueKind.Null, peerList.GetProperty("label").ValueKind);
    }

    [SkippableFact]
    public async Task SaveLabel_ReplacesPreviousPacket()
    {
        await EnsureDatabaseAsync();
        var (owner, _, _, dialogId) = await SetupAsync();
        await owner.Client.PutAsJsonAsync($"/dialogs/{dialogId}/label", new { v = 1, iv = Iv, ct = Ciphertext });

        var next = Enumerable.Range(1, 32).Select(x => (byte)x).ToArray();
        using var replaced = await owner.Client.PutAsJsonAsync($"/dialogs/{dialogId}/label",
            new { v = 1, iv = Iv, ct = next });
        Assert.Equal(HttpStatusCode.NoContent, replaced.StatusCode);

        var own = await GetDialogAsync(owner.Client, dialogId);
        Assert.Equal(32, Base64UrlLength(own.GetProperty("label").GetProperty("ct").GetString()));
    }

    [SkippableFact]
    public async Task ConcurrentSaves_KeepSingleRow()
    {
        await EnsureDatabaseAsync();
        var (owner, _, _, dialogId) = await SetupAsync();
        var first = owner.Client.PutAsJsonAsync($"/dialogs/{dialogId}/label",
            new { v = 1, iv = Iv, ct = Ciphertext });
        var second = owner.Client.PutAsJsonAsync($"/dialogs/{dialogId}/label",
            new { v = 1, iv = Iv, ct = Enumerable.Range(1, 32).Select(x => (byte)x).ToArray() });
        using (var one = await first)
        using (var two = await second)
        {
            Assert.Equal(HttpStatusCode.NoContent, one.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, two.StatusCode);
        }
        await InDatabaseAsync(async db => Assert.Equal(1,
            await db.DialogLabels.CountAsync(l => l.DialogId == dialogId)));
    }

    [SkippableFact]
    public async Task NonParticipant_Get404WithoutDisclosure()
    {
        await EnsureDatabaseAsync();
        var (owner, _, stranger, dialogId) = await SetupAsync();

        using var put = await stranger.Client.PutAsJsonAsync($"/dialogs/{dialogId}/label",
            new { v = 1, iv = Iv, ct = Ciphertext });
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        using var delete = await stranger.Client.DeleteAsync($"/dialogs/{dialogId}/label");
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        using var get = await stranger.Client.GetAsync($"/dialogs/{dialogId}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        // Подпись не появилась.
        var own = await GetDialogAsync(owner.Client, dialogId);
        Assert.Equal(JsonValueKind.Null, own.GetProperty("label").ValueKind);
    }

    [SkippableFact]
    public async Task InvalidPackets_AreRejected()
    {
        await EnsureDatabaseAsync();
        var (owner, _, _, dialogId) = await SetupAsync();
        foreach (var packet in new object[]
                 {
                     new { v = 2, iv = Iv, ct = Ciphertext },
                     new { v = 1, iv = new byte[11], ct = Ciphertext },
                     new { v = 1, iv = Iv, ct = new byte[15] },
                     new { v = 1, iv = Iv, ct = new byte[513] },
                 })
        {
            using var response = await owner.Client.PutAsJsonAsync($"/dialogs/{dialogId}/label", packet);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var own = await GetDialogAsync(owner.Client, dialogId);
        Assert.Equal(JsonValueKind.Null, own.GetProperty("label").ValueKind);
    }

    [SkippableFact]
    public async Task DeleteLabel_IsIdempotent_AndClearsTheField()
    {
        await EnsureDatabaseAsync();
        var (owner, _, _, dialogId) = await SetupAsync();
        await owner.Client.PutAsJsonAsync($"/dialogs/{dialogId}/label", new { v = 1, iv = Iv, ct = Ciphertext });

        using var deleted = await owner.Client.DeleteAsync($"/dialogs/{dialogId}/label");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var own = await GetDialogAsync(owner.Client, dialogId);
        Assert.Equal(JsonValueKind.Null, own.GetProperty("label").ValueKind);

        // Повторное удаление — тоже 204.
        using var again = await owner.Client.DeleteAsync($"/dialogs/{dialogId}/label");
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
    }

    [SkippableFact]
    public async Task Label_WithoutSession_ReturnsUnauthorized()
    {
        await EnsureDatabaseAsync();
        var (owner, _, _, dialogId) = await SetupAsync();
        var anonymous = factory.CreateClient();

        using var put = await anonymous.PutAsJsonAsync($"/dialogs/{dialogId}/label",
            new { v = 1, iv = Iv, ct = Ciphertext });
        using var delete = await anonymous.DeleteAsync($"/dialogs/{dialogId}/label");
        Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, delete.StatusCode);
    }

    [SkippableFact]
    public async Task DeletingAccount_RemovesLabelRow()
    {
        await EnsureDatabaseAsync();
        var (owner, _, _, dialogId) = await SetupAsync();
        await owner.Client.PutAsJsonAsync($"/dialogs/{dialogId}/label", new { v = 1, iv = Iv, ct = Ciphertext });
        await InDatabaseAsync(async db =>
        {
            db.Users.Remove(await db.Users.SingleAsync(u => u.Id == owner.Id));
            await db.SaveChangesAsync();
        });
        await InDatabaseAsync(async db => Assert.False(
            await db.DialogLabels.AnyAsync(l => l.DialogId == dialogId)));
    }

    private async Task<(Actor Owner, Actor Peer, Actor Stranger, Guid DialogId)> SetupAsync()
    {
        var owner = await CreateActorAsync();
        var peer = await CreateActorAsync();
        var stranger = await CreateActorAsync();
        using var created = await owner.Client.PostAsJsonAsync("/dialogs", new { publicId = peer.PublicId });
        created.EnsureSuccessStatusCode();
        var dialog = await created.Content.ReadFromJsonAsync<JsonElement>();
        return (owner, peer, stranger, dialog.GetProperty("id").GetGuid());
    }

    private static int Base64UrlLength(string? encoded)
    {
        var standard = encoded!.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(standard.PadRight((standard.Length + 3) / 4 * 4, '=')).Length;
    }

    private static async Task<JsonElement> GetDialogAsync(HttpClient client, Guid dialogId)
    {
        using var response = await client.GetAsync($"/dialogs/{dialogId}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<Actor> CreateActorAsync()
    {
        var registration = factory.CreateClient();
        var publicId = await RegisterAsync(registration);
        var actor = factory.CreateClient();
        await LoginAsync(actor, publicId);
        var userId = await InDatabaseAsync(async db => await db.Users
            .Where(u => u.PublicId == publicId)
            .Select(u => u.Id)
            .SingleAsync());
        return new Actor(userId, publicId, actor);
    }

    private sealed record Actor(Guid Id, string PublicId, HttpClient Client);

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/auth/register", new { password = Password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<RegisterBody>();
        return body!.PublicId;
    }

    private static async Task LoginAsync(HttpClient client, string publicId)
    {
        var response = await client.PostAsJsonAsync("/auth/login", new { publicId, password = Password });
        response.EnsureSuccessStatusCode();
    }

    private async Task<T> InDatabaseAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        try
        {
            using var scope = factory.Services.CreateScope();
            return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }
        catch (Exception ex)
        {
            Skip.If(true, $"PostgreSQL недоступна ({ex.GetType().Name}: {ex.Message}). Тест пропущен.");
            throw;
        }
    }

    private async Task InDatabaseAsync(Func<AppDbContext, Task> action) =>
        await InDatabaseAsync(async db =>
        {
            await action(db);
            return true;
        });

    private async Task EnsureDatabaseAsync()
    {
        try
        {
            using var scope = factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await context.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            Skip.If(true, $"PostgreSQL недоступна ({ex.GetType().Name}: {ex.Message}). Тест пропущен.");
        }
    }

    private sealed record RegisterBody(string PublicId);
}
