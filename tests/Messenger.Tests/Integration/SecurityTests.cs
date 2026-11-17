using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Messenger.Application.Interfaces;
using Messenger.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Messenger.Tests.Integration;

/// <summary>Реальный HTTP pipeline с тестовыми users/sessions; PostgreSQL не требуется.</summary>
public sealed class SecurityTests
{
    [Theory]
    [InlineData("/health", HttpStatusCode.OK)]
    [InlineData("/unknown", HttpStatusCode.NotFound)]
    [InlineData("/dialogs", HttpStatusCode.Unauthorized)]
    public async Task Headers_ArePresentOnSuccessAndErrors(string path, HttpStatusCode expected)
    {
        using var factory = new SecurityApiFactory();
        using var client = factory.Client();
        using var response = await client.GetAsync(path);

        Assert.Equal(expected, response.StatusCode);
        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("script-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.DoesNotContain("unsafe-inline", csp);
        Assert.DoesNotContain("unsafe-eval", csp);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task LoginAndLogout_SetProtectedCookies_AndRevokeSession()
    {
        using var factory = new SecurityApiFactory();
        using var client = factory.Client();
        using var login = await client.PostAsJsonAsync("/auth/login",
            new { publicId = "TESTUSER", password = "valid-password" });
        login.EnsureSuccessStatusCode();
        var rawCookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        var cookie = rawCookie.ToLowerInvariant();
        Assert.Contains("httponly", cookie);
        Assert.Contains("secure", cookie);
        Assert.Contains("samesite=strict", cookie);
        using var current = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);

        using var logout = await client.PostAsync("/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var deleted = Assert.Single(logout.Headers.GetValues("Set-Cookie")).ToLowerInvariant();
        Assert.Contains("httponly", deleted);
        Assert.Contains("secure", deleted);
        Assert.Contains("samesite=strict", deleted);
        using var after = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);

        // Проверяем и серверный отзыв: повторно подставленный старый токен не работает.
        using var replay = factory.Client(handleCookies: false);
        replay.DefaultRequestHeaders.Add("Cookie", rawCookie.Split(';')[0]);
        using var replayResponse = await replay.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);
    }

    [Fact]
    public async Task AuthLimit_IsSharedByLoginAndRegister_AndIgnoresSpoofedForwardedIp()
    {
        using var factory = new SecurityApiFactory();
        using var client = factory.Client();
        using var first = await client.PostAsJsonAsync("/auth/register", new { password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.5");
        using var second = await client.PostAsJsonAsync("/auth/login", new { });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.6");
        using var rejected = await client.PostAsJsonAsync("/auth/register", new { password = "short" });

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType!.MediaType);
        var body = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("rate_limited", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SearchLimit_Returns429_ForAuthenticatedEnumeration()
    {
        using var factory = new SecurityApiFactory();
        using var client = factory.Client();
        using var login = await client.PostAsJsonAsync("/auth/login",
            new { publicId = "TESTUSER", password = "valid-password" });
        login.EnsureSuccessStatusCode();
        using var first = await client.GetAsync("/users/by-id/TESTUSER");
        using var second = await client.GetAsync("/users/by-id/TESTUSER");
        using var third = await client.GetAsync("/users/by-id/TESTUSER");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    [Theory]
    [InlineData("https://attacker.example", "same-site")]
    [InlineData("null", "same-site")]
    [InlineData("https://localhost", "cross-site")]
    [InlineData("https://localhost:444", "same-site")]
    public async Task CrossOriginMutations_AreRejected(string origin, string fetchSite)
    {
        using var factory = new SecurityApiFactory();
        using var client = factory.Client();
        client.DefaultRequestHeaders.Add("Origin", origin);
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", fetchSite);
        using var response = await client.PostAsJsonAsync("/auth/register", new { password = "short" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SameOriginMutations_ReachValidation()
    {
        using var factory = new SecurityApiFactory();
        using var client = factory.Client();
        client.DefaultRequestHeaders.Add("Origin", "https://localhost");
        using var response = await client.PostAsJsonAsync("/auth/register", new { password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CrossOriginWebSocketHandshake_IsRejected()
    {
        using var factory = new SecurityApiFactory();
        using var client = factory.Client();
        client.DefaultRequestHeaders.Add("Origin", "https://attacker.example");
        using var response = await client.GetAsync("/hubs/messenger");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ProductionHttps_SetsHsts()
    {
        using var factory = new SecurityApiFactory(environment: "Production");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://messenger.example"),
        });
        using var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("max-age=31536000", Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
    }

    private sealed class SecurityApiFactory(string environment = "Development") : WebApplicationFactory<Program>
    {
        public HttpClient Client(bool handleCookies = true) => CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = handleCookies,
        });

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Session:Secure"] = "true",
                    ["RateLimits:AuthPermitLimit"] = "2",
                    ["RateLimits:SearchPermitLimit"] = "2",
                    ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=unused",
                    ["AttachmentStorage:RootPath"] = Path.Combine(Path.GetTempPath(), "messenger-security-tests"),
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IUserRepository>();
                services.RemoveAll<ISessionStore>();
                services.RemoveAll<IPasswordHasher>();
                services.AddSingleton<IUserRepository, TestUsers>();
                services.AddSingleton<ISessionStore, TestSessions>();
                services.AddSingleton<IPasswordHasher, TestPasswordHasher>();
            });
        }
    }

    private sealed class TestUsers : IUserRepository
    {
        private readonly User _user = new()
        {
            Id = Guid.NewGuid(), PublicId = "TESTUSER", PasswordHash = "test-hash", CreatedAt = DateTime.UtcNow,
        };
        public Task<User?> GetByPublicIdAsync(string publicId, CancellationToken cancellationToken = default) =>
            Task.FromResult(publicId == _user.PublicId ? _user : null);
        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(id == _user.Id ? _user : null);
        public Task AddAsync(User user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TestPasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => "test-hash";
        public bool Verify(string password, string encodedHash) => password == "valid-password" && encodedHash == "test-hash";
    }

    private sealed class TestSessions : ISessionStore
    {
        private readonly ConcurrentDictionary<string, Session> _sessions = new();
        public Task<Session> CreateAsync(Guid userId, string tokenHash, DateTime expiresAt,
            CancellationToken cancellationToken = default)
        {
            var session = new Session
            {
                Id = Guid.NewGuid(), UserId = userId, TokenHash = tokenHash,
                CreatedAt = DateTime.UtcNow, ExpiresAt = expiresAt,
            };
            _sessions[tokenHash] = session;
            return Task.FromResult(session);
        }
        public Task<Session?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
            Task.FromResult(_sessions.TryGetValue(tokenHash, out var session) ? session : null);
        public Task<bool> RevokeAsync(string tokenHash, CancellationToken cancellationToken = default) =>
            Task.FromResult(_sessions.TryRemove(tokenHash, out _));
    }
}
