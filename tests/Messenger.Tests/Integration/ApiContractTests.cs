using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Messenger.Api.Contracts;
using Messenger.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Messenger.Tests.Integration;

/// <summary>
/// Проверки контракта API: спецификация доступна, ошибки приходят в
/// едином формате с машинно-читаемым кодом.
/// </summary>
public class ApiContractTests(MessengerApiFactory factory) : IClassFixture<MessengerApiFactory>
{
    private const string Password = "correct horse battery staple";

    [Fact]
    public async Task OpenApiDocument_IsAvailable()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Мессенджер API", document.GetProperty("info").GetProperty("title").GetString());
    }

    [Fact]
    public async Task OpenApiDocument_MessagePacketMatchesCurrentFrontend()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        var schemas = document.GetProperty("components").GetProperty("schemas");
        foreach (var name in new[] { "SendMessageRequest", "MessageResponse" })
        {
            var properties = schemas.GetProperty(name).GetProperty("properties");
            Assert.Equal("integer", properties.GetProperty("sentAt").GetProperty("type").GetString());
            Assert.Equal("int64", properties.GetProperty("sentAt").GetProperty("format").GetString());
            Assert.Equal("base64url", properties.GetProperty("iv").GetProperty("format").GetString());
            Assert.Equal("base64url", properties.GetProperty("ct").GetProperty("format").GetString());
        }
        Assert.Contains("publicId", schemas.GetProperty("MessageResponse").GetProperty("properties")
            .GetProperty("senderId").GetProperty("description").GetString());
    }

    [Fact]
    public async Task OpenApiDocument_ContainsAllEndpoints()
    {
        var client = factory.CreateClient();

        var document = await (await client.GetAsync("/openapi/v1.json"))
            .Content.ReadFromJsonAsync<JsonElement>();

        var paths = document.GetProperty("paths");

        foreach (var path in new[]
                 {
                     "/auth/register",
                     "/auth/login",
                     "/auth/logout",
                     "/auth/me",
                     "/users/by-id/{id}",
                     "/dialogs",
                     "/dialogs/{id}",
                 })
        {
            Assert.True(paths.TryGetProperty(path, out _), $"Путь {path} отсутствует в спецификации.");
        }
    }

    [Fact]
    public async Task OpenApiDocument_CurrentUserIdentifierHasUuidSchema()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        var currentUser = document.GetProperty("components").GetProperty("schemas")
            .GetProperty("CurrentUserResponse");
        var identifier = currentUser.GetProperty("properties").GetProperty("id");

        Assert.Equal("string", identifier.GetProperty("type").GetString());
        Assert.Equal("uuid", identifier.GetProperty("format").GetString());
        Assert.Contains("id", currentUser.GetProperty("required").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task OpenApiDocument_DeclaresErrorCode()
    {
        var client = factory.CreateClient();

        var document = await (await client.GetAsync("/openapi/v1.json"))
            .Content.ReadFromJsonAsync<JsonElement>();

        var code = document
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("ProblemDetails")
            .GetProperty("properties")
            .GetProperty("code");

        var values = code.GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToList();

        Assert.Contains(ApiErrorCodes.ValidationFailed, values);
        Assert.Contains(ApiErrorCodes.Unauthorized, values);
        Assert.Contains(ApiErrorCodes.DialogNotFound, values);
        Assert.Contains(ApiErrorCodes.MessageConflict, values);
        Assert.Contains(ApiErrorCodes.RateLimited, values);
    }

    [Fact]
    public async Task ValidationError_ContainsCode()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/register",
            new { password = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(ApiErrorCodes.ValidationFailed, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unauthorized_ContainsCode()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/dialogs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(ApiErrorCodes.Unauthorized, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UnknownRoute_ReturnsNotFoundWithCode()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/no-such-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(ApiErrorCodes.NotFound, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UnsupportedMediaType_ContainsCode()
    {
        var client = factory.CreateClient();

        using var content = new StringContent("{}", Encoding.UTF8, "text/plain");

        var response = await client.PostAsync("/auth/register", content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(ApiErrorCodes.UnsupportedMediaType, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task MethodNotAllowed_ContainsCode()
    {
        var client = factory.CreateClient();

        var response = await client.PutAsync("/dialogs", null);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(ApiErrorCodes.MethodNotAllowed, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task OpenApiDocument_HasNoContentForNoContentResponse()
    {
        var client = factory.CreateClient();

        var document = await (await client.GetAsync("/openapi/v1.json"))
            .Content.ReadFromJsonAsync<JsonElement>();

        var logout = document.GetProperty("paths").GetProperty("/auth/logout");
        var noContent = logout.GetProperty("post").GetProperty("responses").GetProperty("204");

        Assert.False(noContent.TryGetProperty("content", out _));
    }

    [SkippableFact]
    public async Task NotFoundDialog_ContainsCode()
    {
        await EnsureDatabaseAsync();

        var client = factory.CreateClient();

        var publicId = await RegisterAsync(factory.CreateClient());

        var login = await client.PostAsJsonAsync(
            "/auth/login",
            new { publicId, password = Password });

        login.EnsureSuccessStatusCode();

        var response = await client.GetAsync($"/dialogs/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(ApiErrorCodes.DialogNotFound, problem.GetProperty("code").GetString());
    }

    private static async Task<string> RegisterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/auth/register", new { password = Password });

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<RegisterBody>();

        return body!.PublicId;
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
}
