using System.Net;
using System.Net.Http.Json;
using Messenger.Domain.Entities;
using Messenger.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Messenger.Tests.Integration;

public class DialogCreationTests(MessengerApiFactory factory) : IClassFixture<MessengerApiFactory>
{
    private const string Password = "correct horse battery staple";

    [SkippableFact]
    public async Task Create_WithCompanionPublicId_Returns201()
    {
        await EnsureDatabaseAsync();

        var client = factory.CreateClient();
        var publicId = await RegisterAsync(factory.CreateClient());
        var companionId = await RegisterAsync(factory.CreateClient());

        await LoginAsync(client, publicId);

        var response = await client.PostAsJsonAsync("/dialogs", new { publicId = companionId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<DialogBody>();

        Assert.Equal(companionId, body!.CompanionPublicId);
    }

    [SkippableFact]
    public async Task Create_Twice_ReturnsSameDialogAndCreatesNoDuplicate()
    {
        await EnsureDatabaseAsync();

        var client = factory.CreateClient();
        var publicId = await RegisterAsync(factory.CreateClient());
        var companionId = await RegisterAsync(factory.CreateClient());

        await LoginAsync(client, publicId);

        var first = await client.PostAsJsonAsync("/dialogs", new { publicId = companionId });
        var second = await client.PostAsJsonAsync("/dialogs", new { publicId = companionId });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var firstBody = await first.Content.ReadFromJsonAsync<DialogBody>();
        var secondBody = await second.Content.ReadFromJsonAsync<DialogBody>();

        Assert.Equal(firstBody!.Id, secondBody!.Id);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pairKey = Dialog.PairKeyFor(
            await GetUserIdAsync(context, publicId),
            await GetUserIdAsync(context, companionId));

        var dialogsCount = await context.Dialogs
            .AsNoTracking()
            .CountAsync(d => d.PairKey == pairKey);

        Assert.Equal(1, dialogsCount);
    }

    [SkippableFact]
    public async Task Create_FromBothSides_CreatesOneDialog()
    {
        await EnsureDatabaseAsync();

        var firstClient = factory.CreateClient();
        var secondClient = factory.CreateClient();

        var firstId = await RegisterAsync(factory.CreateClient());
        var secondId = await RegisterAsync(factory.CreateClient());

        await LoginAsync(firstClient, firstId);
        await LoginAsync(secondClient, secondId);

        var fromFirst = await firstClient.PostAsJsonAsync("/dialogs", new { publicId = secondId });
        var fromSecond = await secondClient.PostAsJsonAsync("/dialogs", new { publicId = firstId });

        var firstBody = await fromFirst.Content.ReadFromJsonAsync<DialogBody>();
        var secondBody = await fromSecond.Content.ReadFromJsonAsync<DialogBody>();

        Assert.Equal(firstBody!.Id, secondBody!.Id);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pairKey = Dialog.PairKeyFor(
            await GetUserIdAsync(context, firstId),
            await GetUserIdAsync(context, secondId));

        var dialogsCount = await context.Dialogs
            .AsNoTracking()
            .CountAsync(d => d.PairKey == pairKey);

        Assert.Equal(1, dialogsCount);
    }

    [SkippableFact]
    public async Task Create_ConcurrentlyFromBothSides_CreatesOneDialog()
    {
        await EnsureDatabaseAsync();

        var firstClient = factory.CreateClient();
        var secondClient = factory.CreateClient();

        var firstId = await RegisterAsync(factory.CreateClient());
        var secondId = await RegisterAsync(factory.CreateClient());

        await LoginAsync(firstClient, firstId);
        await LoginAsync(secondClient, secondId);

        var requests = new[]
        {
            firstClient.PostAsJsonAsync("/dialogs", new { publicId = secondId }),
            secondClient.PostAsJsonAsync("/dialogs", new { publicId = firstId }),
        };

        var responses = await Task.WhenAll(requests);

        var ids = new List<Guid>();

        foreach (var response in responses)
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
                $"Неожиданный статус: {response.StatusCode}");

            var body = await response.Content.ReadFromJsonAsync<DialogBody>();

            ids.Add(body!.Id);
        }

        Assert.Equal(ids[0], ids[1]);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pairKey = Dialog.PairKeyFor(
            await GetUserIdAsync(context, firstId),
            await GetUserIdAsync(context, secondId));

        var dialogsCount = await context.Dialogs
            .AsNoTracking()
            .CountAsync(d => d.PairKey == pairKey);

        Assert.Equal(1, dialogsCount);
    }

    [SkippableFact]
    public async Task Create_WithSelf_ReturnsBadRequest()
    {
        await EnsureDatabaseAsync();

        var client = factory.CreateClient();
        var publicId = await RegisterAsync(factory.CreateClient());

        await LoginAsync(client, publicId);

        var response = await client.PostAsJsonAsync("/dialogs", new { publicId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [SkippableFact]
    public async Task Create_WithUnknownCompanion_ReturnsNotFound()
    {
        await EnsureDatabaseAsync();

        var client = factory.CreateClient();
        var publicId = await RegisterAsync(factory.CreateClient());

        await LoginAsync(client, publicId);

        var response = await client.PostAsJsonAsync("/dialogs", new { publicId = "NOSUCHID" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [SkippableFact]
    public async Task Create_WithoutSession_ReturnsUnauthorized()
    {
        await EnsureDatabaseAsync();

        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/dialogs", new { publicId = "ANY" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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
        var response = await client.PostAsJsonAsync("/auth/login", new { publicId, password = Password });

        response.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> GetUserIdAsync(AppDbContext context, string publicId)
    {
        var user = await context.Users
            .AsNoTracking()
            .SingleAsync(u => u.PublicId == publicId);

        return user.Id;
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

    private sealed record DialogBody(Guid Id, string CompanionPublicId, DateTime CreatedAt);
}
