using System.Net;
using System.Net.Http.Json;
using Messenger.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Messenger.Tests.Integration;

public class DialogListTests(MessengerApiFactory factory) : IClassFixture<MessengerApiFactory>
{
    private const string Password = "correct horse battery staple";

    [SkippableFact]
    public async Task List_ReturnsDialogsOfCurrentUser()
    {
        await EnsureDatabaseAsync();

        var client = factory.CreateClient();
        var publicId = await RegisterAsync(factory.CreateClient());
        var companionId = await RegisterAsync(factory.CreateClient());

        await LoginAsync(client, publicId);

        await client.PostAsJsonAsync("/dialogs", new { publicId = companionId });

        var response = await client.GetAsync("/dialogs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var list = await response.Content.ReadFromJsonAsync<List<DialogItem>>();

        var item = Assert.Single(list!);
        Assert.Equal(companionId, item.CompanionPublicId);
    }

    [SkippableFact]
    public async Task List_DoesNotShowDialogsOfOtherUsers()
    {
        await EnsureDatabaseAsync();

        var firstClient = factory.CreateClient();
        var secondClient = factory.CreateClient();
        var thirdClient = factory.CreateClient();

        var firstId = await RegisterAsync(factory.CreateClient());
        var secondId = await RegisterAsync(factory.CreateClient());
        var thirdId = await RegisterAsync(factory.CreateClient());

        await LoginAsync(firstClient, firstId);
        await LoginAsync(secondClient, secondId);
        await LoginAsync(thirdClient, thirdId);

        // Диалог между первым и вторым.
        await firstClient.PostAsJsonAsync("/dialogs", new { publicId = secondId });

        var firstList = await (await firstClient.GetAsync("/dialogs"))
            .Content.ReadFromJsonAsync<List<DialogItem>>();

        var secondList = await (await secondClient.GetAsync("/dialogs"))
            .Content.ReadFromJsonAsync<List<DialogItem>>();

        // У третьего диалогов нет — чужие диалоги не видны.
        var thirdList = await (await thirdClient.GetAsync("/dialogs"))
            .Content.ReadFromJsonAsync<List<DialogItem>>();

        Assert.Single(firstList!);
        Assert.Single(secondList!);
        Assert.Empty(thirdList!);
    }

    [SkippableFact]
    public async Task List_WithoutSession_ReturnsUnauthorized()
    {
        await EnsureDatabaseAsync();

        var client = factory.CreateClient();

        var response = await client.GetAsync("/dialogs");

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