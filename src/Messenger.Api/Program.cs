using Messenger.Api.Authentication;
using Messenger.Api.Configuration;
using Messenger.Api.Middleware;
using Messenger.Api.OpenApi;
using Messenger.Api.Realtime;
using Messenger.Application;
using Messenger.Application.Options;
using Messenger.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Messenger.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Предел тела запроса выше лимита вложения: превышение 25 МБ должен
// отклонять сам сервер — с понятным сообщением и кодом ошибки, а не
// веб-сервер.
builder.WebHost.ConfigureKestrel(options => { options.Limits.MaxRequestBodySize = 64L * 1024 * 1024; });

builder.Services.AddOpenApi(
    ApiOpenApiConfiguration.DocumentName,
    ApiOpenApiConfiguration.Configure);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.Configure<SessionCookieOptions>(builder.Configuration.GetSection(SessionCookieOptions.SectionName));

builder.Services.AddControllers(options =>
{
    // Ошибки, которые MVC формирует сам, приводим к общему виду:
    // русский текст и машинно-читаемый код.
    options.Filters.Add<ApiProblemCodeFilter>();
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApplicationExceptionHandler>();

// Ошибки валидации — на русском, единым форматом ProblemDetails.
builder.Services.Configure<ApiBehaviorOptions>(options =>
    options.InvalidModelStateResponseFactory = ValidationProblemFactory.Create);

builder.Services
    .AddAuthentication(SessionAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(
        SessionAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization();
builder.Services.AddMessengerSecurity(builder.Configuration);
builder.Services.AddMessengerRealtime();

var app = builder.Build();

ValidateSessionOptions(app.Configuration, app.Environment);

// Opt-in for the single-instance local Compose deployment; production can apply migrations separately.
if (app.Configuration.GetValue<bool>("Database:ApplyMigrations"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

// JSON-документ спецификации доступен во всех средах: фронтенду нужен
// стабильный контракт. Интерактивный UI не отдаём.
app.MapOpenApi();
app.UseMiddleware<SecurityHeadersMiddleware>();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseExceptionHandler();
app.UseStatusCodePages(async (StatusCodeContext statusCodeContext) =>
{
    // Ответы без тела (например, неизвестный маршрут или неверный метод)
    // приводим к тому же формату ошибки, что и остальные ответы.
    var context = statusCodeContext.HttpContext;
    var status = context.Response.StatusCode;

    var (title, detail, code) = ApiProblem.ForStatus(status);

    var problem = ApiProblem.Create(status, title, detail, code);

    await context.Response.WriteAsJsonAsync(
        problem,
        options: null,
        contentType: "application/problem+json",
        cancellationToken: context.RequestAborted);
});
app.UseHttpsRedirection();
app.UseMiddleware<SameOriginMiddleware>();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapMessengerRealtime();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("HealthCheck");

app.Run();

// Некорректная конфигурация cookie ломает безопасность молча,
// поэтому падаем сразу при старте.
static void ValidateSessionOptions(IConfiguration configuration, IWebHostEnvironment environment)
{
    var options = configuration
        .GetSection(SessionCookieOptions.SectionName)
        .Get<SessionCookieOptions>() ?? new SessionCookieOptions();

    if (options.SameSite.Equals("None", StringComparison.OrdinalIgnoreCase) && !options.Secure)
    {
        throw new InvalidOperationException(
            "SameSite=None требует Secure=true: браузер отвергнет cookie без флага Secure.");
    }

    if (options.LifetimeHours <= 0)
    {
        throw new InvalidOperationException("Session:LifetimeHours должен быть больше нуля.");
    }

    if (!new[] { "Strict", "Lax", "None" }.Contains(options.SameSite, StringComparer.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Session:SameSite должен быть Strict, Lax или None.");
    }

    if (!environment.IsDevelopment() && !options.Secure)
    {
        throw new InvalidOperationException("Session:Secure должен быть true вне среды Development.");
    }

    if (string.IsNullOrWhiteSpace(options.CookieName) || options.CookieName.Any(character =>
            !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
    {
        throw new InvalidOperationException("Session:CookieName содержит недопустимые символы.");
    }
}

/// <summary>Доступно для WebApplicationFactory в интеграционных тестах.</summary>
public partial class Program;
