using System.Net;
using System.Net.Http.Json;
using Messenger.Application.Services;
using Messenger.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Messenger.Tests.Integration;

public class UserSearchTests(MessengerApiFactory factory) : IClassFixture<MessengerApiFactory>
{
    private const string Password = "correct horse battery staple";

    [SkippableFact]
    public async Task ById_WithExactPublicId_ReturnsUser()
    {
        await EnsureDatabaseAsync();

        var client = factory.CreateClient();
        var publicId = await RegisterAsync(client);

        await LoginAsync(client, publicId);

        var response = await client.GetAsync($"/users/by-id/{publicId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<SearchBody>();

        Assert.Equal(publicId, body!.PublicId);
    }

    [SkippableFact]
    public async Task ById_WithPartialPublicId_ReturnsNotFound()
    {
        await EnsureDatabaseAsync();

        var client = factory.CreateClient();
        var publicId = await RegisterAsync(client);

        await LoginAsync(client, publicId);

        var response = await client.GetAsync($"/users/by-id/{publicId[..6]}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [SkippableFact]
    public async Task ById_WithDifferentCase_ReturnsNotFound()
    {
        await EnsureDatabaseAsync();

        var client = factory.CreateClient();
        var publicId = await RegisterAsync(client);

        await LoginAsync(client, publicId);

        var response = await client.GetAsync($"/users/by-id/{publicId.ToLowerInvariant()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [SkippableFact]
    public async Task ById_WithoutSession_ReturnsUnauthorized()
    {
        await EnsureDatabaseAsync();

        var client = factory.CreateClient();
        var publicId = await RegisterAsync(client);

        var response = await client.GetAsync($"/users/by-id/{publicId}");

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

    private sealed record SearchBody(string PublicId, DateTime CreatedAt);
}
