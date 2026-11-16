using System.Net;
using System.Net.Http.Json;
using Messenger.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Messenger.Tests.Integration;

public class DialogAccessTests(MessengerApiFactory factory) : IClassFixture<MessengerApiFactory>
{
    private const string Password = "correct horse battery staple";

    [SkippableFact]
    public async Task GetById_ForParticipant_ReturnsDialog()
    {
        await EnsureDatabaseAsync();

        var client = factory.CreateClient();
        var publicId = await RegisterAsync(factory.CreateClient());
        var companionId = await RegisterAsync(factory.CreateClient());

        await LoginAsync(client, publicId);

        var created = await client.PostAsJsonAsync("/dialogs", new { publicId = companionId });
        var dialog = await created.Content.ReadFromJsonAsync<DialogItem>();

        var response = await client.GetAsync($"/dialogs/{dialog!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<DialogItem>();

        Assert.Equal(companionId, body!.CompanionPublicId);
    }

    [SkippableFact]
    public async Task GetById_ForStranger_ReturnsNotFound()
    {
        await EnsureDatabaseAsync();

        var ownerClient = factory.CreateClient();
        var strangerClient = factory.CreateClient();

        var publicId = await RegisterAsync(factory.CreateClient());
        var companionId = await RegisterAsync(factory.CreateClient());
        var strangerId = await RegisterAsync(factory.CreateClient());

        await LoginAsync(ownerClient, publicId);
        await LoginAsync(strangerClient, strangerId);

        var created = await ownerClient.PostAsJsonAsync("/dialogs", new { publicId = companionId });
        var dialog = await created.Content.ReadFromJsonAsync<DialogItem>();

        // Чужой dialogId не раскрывает данные диалога.
        var response = await strangerClient.GetAsync($"/dialogs/{dialog!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(publicId, body);
        Assert.DoesNotContain(companionId, body);
    }

    [SkippableFact]
    public async Task GetById_WithUnknownId_ReturnsNotFound()
    {
        await EnsureDatabaseAsync();

        var client = factory.CreateClient();
        var publicId = await RegisterAsync(factory.CreateClient());

        await LoginAsync(client, publicId);

        var response = await client.GetAsync($"/dialogs/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [SkippableFact]
    public async Task GetById_WithoutSession_ReturnsUnauthorized()
    {
        // Без сессии проверка отклоняется до обращения к базе.
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/dialogs/{Guid.NewGuid()}");

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

    private sealed record DialogItem(Guid Id, string CompanionPublicId, DateTime CreatedAt);
}