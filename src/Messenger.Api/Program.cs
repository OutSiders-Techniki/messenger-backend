using Messenger.Api.Authentication;
using Messenger.Api.Configuration;
using Messenger.Api.Middleware;
using Messenger.Application;
using Messenger.Application.Options;
using Messenger.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.Configure<SessionCookieOptions>(builder.Configuration.GetSection(SessionCookieOptions.SectionName));

builder.Services.AddControllers();
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

var app = builder.Build();

ValidateSessionOptions(app.Configuration);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("HealthCheck");

app.Run();

/// <summary>
/// Некорректная конфигурация cookie ломает безопасность молча,
/// поэтому падаем сразу при старте.
/// </summary>
static void ValidateSessionOptions(IConfiguration configuration)
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
}

/// <summary>Доступно для WebApplicationFactory в интеграционных тестах.</summary>
public partial class Program;
