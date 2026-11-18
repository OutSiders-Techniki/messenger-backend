using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Messenger.Tests.Integration;

/// <summary>
/// Поднимает реальное приложение в памяти с подключением к тестовой БД.
/// </summary>
public class MessengerApiFactory : WebApplicationFactory<Program>
{
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("MESSENGER_TEST_DB")
        ?? "Host=localhost;Port=5432;Database=messenger;Username=messenger;Password=messenger";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = ConnectionString,

                // Тесты идут по http, поэтому Secure-cookie выключаем.
                ["Session:Secure"] = "false",
                // Лимиты проверяются отдельно; общий fixture не должен
                // ограничивать регистрацию данных остальных тестов.
                ["RateLimits:AuthPermitLimit"] = "10000",
                ["RateLimits:SearchPermitLimit"] = "10000",

                // Зашифрованные байты тестов пишем во временный каталог,
                // рабочее хранилище не трогаем.
                ["AttachmentStorage:RootPath"] = Path.Combine(
                    Path.GetTempPath(),
                    "messenger-tests-attachments"),
            }));
    }
}
