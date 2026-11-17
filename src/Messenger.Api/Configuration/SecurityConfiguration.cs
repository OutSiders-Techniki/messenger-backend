using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Messenger.Api.Configuration;

public static class SecurityConfiguration
{
    public const string AuthPolicy = "auth";
    public const string SearchPolicy = "search";

    public static IServiceCollection AddMessengerSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRateLimiter(options =>
        {
            // Настройки читаем при создании options: конфигурация тестового
            // или внешнего host может добавлять overrides после регистрации.
            var authLimit = ReadPositive(configuration, "RateLimits:AuthPermitLimit", 10);
            var authWindow = ReadPositive(configuration, "RateLimits:AuthWindowSeconds", 900);
            var searchLimit = ReadPositive(configuration, "RateLimits:SearchPermitLimit", 30);
            var searchWindow = ReadPositive(configuration, "RateLimits:SearchWindowSeconds", 60);

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AuthPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => Window(authLimit, authWindow)));
            options.AddPolicy(SearchPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                $"{context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous"}:{context.Connection.RemoteIpAddress}",
                _ => Window(searchLimit, searchWindow)));
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                        .ToString(CultureInfo.InvariantCulture);
                }

                var (title, detail, code) = ApiProblem.ForStatus(StatusCodes.Status429TooManyRequests);
                await context.HttpContext.Response.WriteAsJsonAsync(
                    ApiProblem.Create(StatusCodes.Status429TooManyRequests, title, detail, code),
                    options: null,
                    contentType: "application/problem+json",
                    cancellationToken: cancellationToken);
            };
        });
        services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(365);
        });
        return services;
    }

    private static FixedWindowRateLimiterOptions Window(int limit, int seconds) => new()
    {
        PermitLimit = limit,
        Window = TimeSpan.FromSeconds(seconds),
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    private static int ReadPositive(IConfiguration configuration, string key, int fallback)
    {
        var value = configuration.GetValue<int?>(key) ?? fallback;
        return value > 0 ? value : throw new InvalidOperationException($"{key} должен быть больше нуля.");
    }
}
