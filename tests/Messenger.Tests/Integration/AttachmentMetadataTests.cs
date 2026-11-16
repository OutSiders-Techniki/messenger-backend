using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Messenger.Api.Contracts;
using Messenger.Domain.Entities;
using Messenger.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Messenger.Tests.Integration;

/// <summary>
/// Проверки контракта метаданных вложений: доступ только владельцу копии
/// сообщения, чужое вложение не раскрывается.
/// </summary>
public class AttachmentMetadataTests(MessengerApiFactory factory) : IClassFixture<MessengerApiFactory>
{
    private const string Password = "correct horse battery staple";

    [SkippableFact]
    public async Task GetById_ForOwner_ReturnsMetadata()
    {
        await EnsureDatabaseAsync();

        var ownerClient = factory.CreateClient();

        var ownerId = await RegisterAsync(factory.CreateClient());
        var companionId = await RegisterAsync(factory.CreateClient());

        await LoginAsync(ownerClient, ownerId);

        var seeded = await SeedAttachmentAsync(ownerId, companionId);

        var response = await ownerClient.GetAsync(
            $"/dialogs/{seeded.DialogId}/messages/{seeded.ClientMessageId}"
            + $"/attachments/{seeded.ClientAttachmentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AttachmentBody>();

        Assert.Equal(seeded.ClientMessageId, body!.ClientMessageId);
        Assert.Equal(seeded.ClientAttachmentId, body.ClientAttachmentId);
        Assert.Equal(1024, body.Size);
    }

    [SkippableFact]
    public async Task GetById_ForStranger_ReturnsNotFound()
    {
        await EnsureDatabaseAsync();

        var ownerClient = factory.CreateClient();
        var strangerClient = factory.CreateClient();

        var ownerId = await RegisterAsync(factory.CreateClient());
        var companionId = await RegisterAsync(factory.CreateClient());
        var strangerId = await RegisterAsync(factory.CreateClient());

        await LoginAsync(ownerClient, ownerId);
        await LoginAsync(strangerClient, strangerId);

        var seeded = await SeedAttachmentAsync(ownerId, companionId);

        // Чужое вложение не раскрывается: ответ тот же, что для несуществующего.
        var response = await strangerClient.GetAsync(
            $"/dialogs/{seeded.DialogId}/messages/{seeded.ClientMessageId}"
            + $"/attachments/{seeded.ClientAttachmentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(ApiErrorCodes.AttachmentNotFound, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetById_WithoutSession_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/dialogs/{Guid.NewGuid()}/messages/{Guid.NewGuid()}/attachments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SkippableFact]
    public async Task List_ForOwner_ReturnsAttachments()
    {
        await EnsureDatabaseAsync();

        var ownerClient = factory.CreateClient();

        var ownerId = await RegisterAsync(factory.CreateClient());
        var companionId = await RegisterAsync(factory.CreateClient());

        await LoginAsync(ownerClient, ownerId);

        var seeded = await SeedAttachmentAsync(ownerId, companionId);

        var response = await ownerClient.GetAsync(
            $"/dialogs/{seeded.DialogId}/messages/{seeded.ClientMessageId}/attachments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var list = await response.Content.ReadFromJsonAsync<List<AttachmentBody>>();

        var item = Assert.Single(list!);
        Assert.Equal(seeded.ClientAttachmentId, item.ClientAttachmentId);
        Assert.Equal(seeded.ClientMessageId, item.ClientMessageId);
    }

    [SkippableFact]
    public async Task List_ForStranger_ReturnsEmpty()
    {
        await EnsureDatabaseAsync();

        var ownerClient = factory.CreateClient();
        var strangerClient = factory.CreateClient();

        var ownerId = await RegisterAsync(factory.CreateClient());
        var companionId = await RegisterAsync(factory.CreateClient());
        var strangerId = await RegisterAsync(factory.CreateClient());

        await LoginAsync(ownerClient, ownerId);
        await LoginAsync(strangerClient, strangerId);

        var seeded = await SeedAttachmentAsync(ownerId, companionId);

        var response = await strangerClient.GetAsync(
            $"/dialogs/{seeded.DialogId}/messages/{seeded.ClientMessageId}/attachments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var list = await response.Content.ReadFromJsonAsync<List<AttachmentBody>>();

        Assert.Empty(list!);
    }

    /// <summary>
    /// Заносит сообщение с вложением напрямую в базу: отправка сообщений
    /// в API ещё не реализована.
    /// </summary>
    private async Task<SeededAttachment> SeedAttachmentAsync(
        string ownerPublicId,
        string companionPublicId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var owner = await context.Users.SingleAsync(u => u.PublicId == ownerPublicId);
        var companion = await context.Users.SingleAsync(u => u.PublicId == companionPublicId);

        var dialog = new Dialog
        {
            Id = Guid.NewGuid(),
            PairKey = Dialog.PairKeyFor(owner.Id, companion.Id),
            CreatedAt = DateTime.UtcNow,
        };

        dialog.Participants.Add(new DialogParticipant
        {
            DialogId = dialog.Id,
            UserId = owner.Id,
            JoinedAt = dialog.CreatedAt,
            User = owner,
        });

        dialog.Participants.Add(new DialogParticipant
        {
            DialogId = dialog.Id,
            UserId = companion.Id,
            JoinedAt = dialog.CreatedAt,
            User = companion,
        });

        var clientMessageId = Guid.NewGuid();

        var message = new Message
        {
            Id = Guid.NewGuid(),
            DialogId = dialog.Id,
            SenderId = owner.Id,
            OwnerId = owner.Id,
            ClientMessageId = clientMessageId,
            Version = 1,
            SentAt = DateTime.UtcNow,
            Iv = new byte[12],
            Ciphertext = [1, 2, 3],
            Dialog = dialog,
            Sender = owner,
            Owner = owner,
        };

        var clientAttachmentId = Guid.NewGuid();

        var attachment = new Attachment
        {
            Id = Guid.NewGuid(),
            MessageId = message.Id,
            ClientAttachmentId = clientAttachmentId,
            StorageKey = $"attachments/{message.Id}/{Guid.NewGuid()}.bin",
            Size = 1024,
            EncryptedMetadata = [7, 8, 9],
            UploadedAt = DateTime.UtcNow,
            Message = message,
        };

        context.Attachments.Add(attachment);
        await context.SaveChangesAsync();

        return new SeededAttachment(dialog.Id, clientMessageId, clientAttachmentId);
    }

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/auth/register", new { password = Password });

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<RegisterBody>();

        return body!.PublicId;
    }

    private static async Task LoginAsync(HttpClient client, string publicId)
    {
        var response = await client.PostAsJsonAsync(
            "/auth/login",
            new { publicId, password = Password });

        response.EnsureSuccessStatusCode();
    }

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
            Skip.If(
                true,
                $"PostgreSQL недоступна ({ex.GetType().Name}: {ex.Message}). Тест пропущен.");
        }
    }

    private sealed record RegisterBody(string PublicId);

    private sealed record AttachmentBody(
        Guid ClientMessageId,
        Guid ClientAttachmentId,
        long Size,
        DateTime UploadedAt,
        string EncryptedMetadata);

    private sealed record SeededAttachment(
        Guid DialogId,
        Guid ClientMessageId,
        Guid ClientAttachmentId);
}