using Messenger.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Tests.Integration;

/// <summary>
/// Подключается к PostgreSQL для интеграционных тестов.
/// Если БД недоступна или миграции не применены — тесты пропускаются.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string DefaultConnectionString = "Host=localhost;Port=5432;Database=messenger;Username=messenger;Password=messenger";

    private AppDbContext? _context;

    public bool Available => _context is not null;

    public AppDbContext Context => _context
        ?? throw new InvalidOperationException("PostgreSQL недоступна.");

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("MESSENGER_TEST_DB")
            ?? DefaultConnectionString;

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(connectionString)
                .UseSnakeCaseNamingConvention()
                .Options;

            var context = new AppDbContext(options);

            // Проверяем, что схема есть (миграции применены)
            await context.Users.AsNoTracking().AnyAsync();

            _context = context;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[Integration] PostgreSQL недоступна ({ex.GetType().Name}: {ex.Message}). " +
                "Интеграционные тесты пропущены. " +
                "Поднимите БД: docker compose up -d postgres && " +
                "dotnet dotnet-ef database update --project src/Messenger.Infrastructure " +
                "--startup-project src/Messenger.Api");
        }
    }

    public async Task DisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }
}
