using System.Net;
using System.Net.Http.Json;
using Messenger.Application.Services;
using Messenger.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Messenger.Tests.Integration;

public class AuthFlowTests(MessengerApiFactory factory) : IClassFixture<MessengerApiFactory>
{
    private const string Password = "correct horse battery staple";

    [SkippableFact]
    public async Task AfterLogout_ProtectedRequestReturns401()
    {
        await EnsureDatabaseAsync();

        var client = factory.CreateClient();

        var publicId = await RegisterAsync(client);
        var cookie = await LoginAsync(client, publicId);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/auth/me")).StatusCode);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.PostAsync("/auth/logout", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/auth/me")).StatusCode);

        // Клиент теряет cookie сам, поэтому проверяем отзыв на сервере:
        // старый токен в cookie больше не должен работать.
        using var replay = factory.CreateClient();
        replay.DefaultRequestHeaders.Add("Cookie", $"messenger_session={cookie}");

        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.GetAsync("/auth/me")).StatusCode);
    }

    [SkippableFact]
    public async Task AfterLogout_SessionRowIsDeletedFromDatabase()
    {
        await EnsureDatabaseAsync();

        var client = factory.CreateClient();

        var publicId = await RegisterAsync(client);
        var cookie = await LoginAsync(client, publicId);

        await client.PostAsync("/auth/logout", null);

        var hash = SessionTokenGenerator.Hash(cookie);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var sessionsLeft = await context.Sessions
            .AsNoTracking()
            .CountAsync(s => s.TokenHash == hash);

        Assert.Equal(0, sessionsLeft);
    }

    [SkippableFact]
    public async Task ConcurrentLogout_DoesNotReturnServerError()
    {
        await EnsureDatabaseAsync();

        var client = factory.CreateClient();

        var publicId = await RegisterAsync(client);
        var cookie = await LoginAsync(client, publicId);

        var logouts = Enumerable.Range(0, 5).Select(_ =>
        {
            var concurrent = factory.CreateClient();
            concurrent.DefaultRequestHeaders.Add("Cookie", $"messenger_session={cookie}");

            return concurrent.PostAsync("/auth/logout", null);
        });

        var responses = await Task.WhenAll(logouts);

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
    }

    [SkippableFact]
    public async Task WithoutLogin_ProtectedRequestReturns401()
    {
        await EnsureDatabaseAsync();

        var client = factory.CreateClient();

        var response = await client.GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SkippableFact]
    public async Task MalformedCookie_DoesNotBreakEndpoints()
    {
        await EnsureDatabaseAsync();

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", "messenger_session=!!!");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [SkippableFact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        await EnsureDatabaseAsync();

        var client = factory.CreateClient();
        var publicId = await RegisterAsync(client);

        var response = await client.PostAsJsonAsync(
            "/auth/login",
            new { publicId, password = "wrong-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [SkippableFact]
    public async Task Login_SetsHttpOnlyCookie()
    {
        await EnsureDatabaseAsync();

        var client = factory.CreateClient();
        var publicId = await RegisterAsync(client);

        var response = await client.PostAsJsonAsync(
            "/auth/login",
            new { publicId, password = Password });

        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            c => c.StartsWith("messenger_session=", StringComparison.Ordinal));

        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task ValidationError_IsReturnedInRussian()
    {
        await EnsureDatabaseAsync();

        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/login", new { publicId = "ABC" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("Укажите пароль.", body);
    }

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/auth/register", new { password = Password });

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<RegisterBody>();

        return body!.PublicId;
    }

    private static async Task<string> LoginAsync(HttpClient client, string publicId)
    {
        var response = await client.PostAsJsonAsync("/auth/login", new { publicId, password = Password });

        response.EnsureSuccessStatusCode();

        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));

        return setCookie
            .Split(';')[0]
            .Split('=', 2)[1];
    }

    /// <summary>
    /// Пропускает тест, если PostgreSQL недоступна: без БД интеграционные
    /// проверки невозможны, но и падать из-за отсутствия сервиса не нужно.
    /// </summary>
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
}
