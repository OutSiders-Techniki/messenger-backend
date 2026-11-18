using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Messenger.Api.Contracts;
using Messenger.Application;
using Messenger.Domain.Entities;
using Messenger.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Messenger.Tests.Integration;

/// <summary>
/// Проверки загрузки зашифрованного вложения: лимит 25 МБ, доступ только
/// к своей копии сообщения, MIME клиента не используется.
/// </summary>
public class AttachmentUploadTests(MessengerApiFactory factory) : IClassFixture<MessengerApiFactory>
{
    private const string Password = "correct horse battery staple";

    [SkippableFact]
    public async Task Upload_ToOwnMessage_ReturnsCreatedAndMetadata()
    {
        await EnsureDatabaseAsync();

        var client = factory.CreateClient();

        var ownerId = await RegisterAsync(factory.CreateClient());
        var companionId = await RegisterAsync(factory.CreateClient());

        await LoginAsync(client, ownerId);

        var seeded = await SeedMessageCopyAsync(ownerId, companionId);
        var clientAttachmentId = Guid.NewGuid();

        var response = await UploadAsync(
            client,
            seeded,
            clientAttachmentId,
            [1, 2, 3, 4],
            [7, 8, 9]);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AttachmentBody>();

        Assert.Equal(seeded.ClientMessageId, body!.ClientMessageId);
        Assert.Equal(clientAttachmentId, body.ClientAttachmentId);
        Assert.Equal(4, body.Size);

        // Вложение видно в списке вложений своей копии сообщения.
        var list = await client.GetAsync(
            $"/dialogs/{seeded.DialogId}/messages/{seeded.ClientMessageId}/attachments");

        var items = await list.Content.ReadFromJsonAsync<List<AttachmentBody>>();

        var item = Assert.Single(items!);
        Assert.Equal(clientAttachmentId, item.ClientAttachmentId);
    }

    [SkippableFact]
    public async Task Upload_ToForeignMessage_ReturnsNotFound()
    {
        await EnsureDatabaseAsync();

        var client = factory.CreateClient();
        var strangerClient = factory.CreateClient();

        var ownerId = await RegisterAsync(factory.CreateClient());
        var companionId = await RegisterAsync(factory.CreateClient());
        var strangerId = await RegisterAsync(factory.CreateClient());

        await LoginAsync(strangerClient, strangerId);

        var seeded = await SeedMessageCopyAsync(ownerId, companionId);

        // Чужая копия сообщения не раскрывается: тот же ответ, что для
        // несуществующего вложения.
        var response = await UploadAsync(
            strangerClient,
            seeded,
            Guid.NewGuid(),
            [1, 2, 3],
            [7]);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(ApiErrorCodes.AttachmentNotFound, problem.GetProperty("code").GetString());
    }

    [SkippableFact]
    public async Task Upload_LargerThanLimit_ReturnsPayloadTooLarge()
    {
        await EnsureDatabaseAsync();

        var client = factory.CreateClient();

        var ownerId = await RegisterAsync(factory.CreateClient());
        var companionId = await RegisterAsync(factory.CreateClient());

        await LoginAsync(client, ownerId);

        var seeded = await SeedMessageCopyAsync(ownerId, companionId);

        var oversized = new byte[MediaLimits.MaxAttachmentSizeBytes + 1];

        var response = await UploadAsync(client, seeded, Guid.NewGuid(), oversized, [7]);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(ApiErrorCodes.AttachmentTooLarge, problem.GetProperty("code").GetString());
    }

    [SkippableFact]
    public async Task Upload_WithoutMetadata_ReturnsBadRequest()
    {
        await EnsureDatabaseAsync();

        var client = factory.CreateClient();

        var ownerId = await RegisterAsync(factory.CreateClient());
        var companionId = await RegisterAsync(factory.CreateClient());

        await LoginAsync(client, ownerId);

        var seeded = await SeedMessageCopyAsync(ownerId, companionId);

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/dialogs/{seeded.DialogId}/messages/{seeded.ClientMessageId}"
            + $"/attachments/{Guid.NewGuid()}")
        {
            Content = new ByteArrayContent([1, 2, 3]),
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(ApiErrorCodes.ValidationFailed, problem.GetProperty("code").GetString());
    }

    [SkippableFact]
    public async Task Upload_TwiceWithSameClientId_ReturnsConflict()
    {
        await EnsureDatabaseAsync();

        var client = factory.CreateClient();

        var ownerId = await RegisterAsync(factory.CreateClient());
        var companionId = await RegisterAsync(factory.CreateClient());

        await LoginAsync(client, ownerId);

        var seeded = await SeedMessageCopyAsync(ownerId, companionId);
        var clientAttachmentId = Guid.NewGuid();

        var first = await UploadAsync(client, seeded, clientAttachmentId, [1, 2], [7]);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await UploadAsync(client, seeded, clientAttachmentId, [1, 2], [7]);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var problem = await second.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(ApiErrorCodes.DuplicateAttachment, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Upload_WithoutSession_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/dialogs/{Guid.NewGuid()}/messages/{Guid.NewGuid()}"
            + $"/attachments/{Guid.NewGuid()}")
        {
            Content = new ByteArrayContent([1, 2, 3]),
        };

        request.Headers.Add("X-Attachment-Metadata", Convert.ToBase64String([7]));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SkippableFact]
    public async Task Download_FromOwnCopy_ReturnsUploadedBytes()
    {
        await EnsureDatabaseAsync();

        var client = factory.CreateClient();

        var ownerId = await RegisterAsync(factory.CreateClient());
        var companionId = await RegisterAsync(factory.CreateClient());

        await LoginAsync(client, ownerId);

        var seeded = await SeedMessageCopyAsync(ownerId, companionId);
        var clientAttachmentId = Guid.NewGuid();
        var content = new byte[] { 10, 20, 30, 40, 50 };

        var upload = await UploadAsync(client, seeded, clientAttachmentId, content, [7]);

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);

        var download = await client.GetAsync(
            $"/dialogs/{seeded.DialogId}/messages/{seeded.ClientMessageId}"
            + $"/attachments/{clientAttachmentId}/content");

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(await download.Content.ReadAsByteArrayAsync(), content);
        // Байты вложения не должны попадать в кэш браузера.
        Assert.True(download.Headers.CacheControl.NoStore);
    }

    [SkippableFact]
    public async Task Upload_FromSender_SharesContentWithExistingRecipientCopy()
    {
        await EnsureDatabaseAsync();

        using var sender = factory.CreateClient();
        using var recipient = factory.CreateClient();
        var senderId = await RegisterAsync(sender);
        var recipientId = await RegisterAsync(recipient);
        await LoginAsync(sender, senderId);
        await LoginAsync(recipient, recipientId);

        var seeded = await SeedMessageCopyAsync(senderId, recipientId, includeRecipientCopy: true);
        var attachmentId = Guid.NewGuid();
        byte[] content = [10, 20, 30];
        var upload = await UploadAsync(sender, seeded, attachmentId, content, [7]);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);

        var messagePath = $"/dialogs/{seeded.DialogId}/messages";
        var filePath = $"{messagePath}/{seeded.ClientMessageId}/attachments/{attachmentId}/content";
        var download = await recipient.GetAsync(filePath);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(content, await download.Content.ReadAsByteArrayAsync());
        var deniedUpload = await UploadAsync(recipient, seeded, Guid.NewGuid(), content, [7]);
        Assert.Equal(HttpStatusCode.NotFound, deniedUpload.StatusCode);

        var ids = new { messageIds = new[] { seeded.ClientMessageId } };
        var deleteSender = await sender.PostAsJsonAsync($"{messagePath}/delete-self", ids);
        Assert.Equal(HttpStatusCode.NoContent, deleteSender.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await sender.GetAsync(filePath)).StatusCode);
        Assert.Equal(content, await recipient.GetByteArrayAsync(filePath));

        var deleteRecipient = await recipient.PostAsJsonAsync($"{messagePath}/delete-self", ids);
        Assert.Equal(HttpStatusCode.NoContent, deleteRecipient.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await recipient.GetAsync(filePath)).StatusCode);
    }

    [SkippableFact]
    public async Task Download_FromForeignCopy_ReturnsNotFound()
    {
        await EnsureDatabaseAsync();

        var strangerClient = factory.CreateClient();

        var ownerId = await RegisterAsync(factory.CreateClient());
        var companionId = await RegisterAsync(factory.CreateClient());
        var strangerId = await RegisterAsync(factory.CreateClient());

        await LoginAsync(strangerClient, strangerId);

        var seeded = await SeedMessageCopyAsync(ownerId, companionId);
        var clientAttachmentId = Guid.NewGuid();

        var ownerClient = factory.CreateClient();
        await LoginAsync(ownerClient, ownerId);
        var upload = await UploadAsync(ownerClient, seeded, clientAttachmentId, [1, 2], [7]);

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);

        // Чужая копия сообщения не раскрывается: тот же ответ, что для
        // несуществующего вложения.
        var download = await strangerClient.GetAsync(
            $"/dialogs/{seeded.DialogId}/messages/{seeded.ClientMessageId}"
            + $"/attachments/{clientAttachmentId}/content");

        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
    }

    [Fact]
    public async Task Download_WithoutSession_ReturnsUnauthorized()
    {
        var response = await factory.CreateClient().GetAsync(
            $"/dialogs/{Guid.NewGuid()}/messages/{Guid.NewGuid()}"
            + $"/attachments/{Guid.NewGuid()}/content");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        SeededMessage seeded,
        Guid clientAttachmentId,
        byte[] content,
        byte[] encryptedMetadata)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/dialogs/{seeded.DialogId}/messages/{seeded.ClientMessageId}"
            + $"/attachments/{clientAttachmentId}")
        {
            Content = new ByteArrayContent(content),
        };

        request.Headers.Add(
            "X-Attachment-Metadata",
            Convert.ToBase64String(encryptedMetadata));

        return await client.SendAsync(request);
    }

    /// <summary>
    /// Заносит копию сообщения напрямую в базу.
    /// </summary>
    private async Task<SeededMessage> SeedMessageCopyAsync(
        string ownerPublicId,
        string companionPublicId,
        bool includeRecipientCopy = false)
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

        context.Messages.Add(message);
        if (includeRecipientCopy)
        {
            context.Messages.Add(new Message
            {
                Id = Guid.NewGuid(),
                DialogId = dialog.Id,
                SenderId = owner.Id,
                OwnerId = companion.Id,
                ClientMessageId = clientMessageId,
                Version = message.Version,
                SentAt = message.SentAt,
                Iv = message.Iv,
                Ciphertext = message.Ciphertext,
                Dialog = dialog,
                Sender = owner,
                Owner = companion,
            });
        }
        await context.SaveChangesAsync();

        return new SeededMessage(dialog.Id, clientMessageId);
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

    private sealed record SeededMessage(
        Guid DialogId,
        Guid ClientMessageId);
}
