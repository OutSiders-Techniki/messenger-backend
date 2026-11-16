using System.Security.Claims;
using System.Text.Encodings.Web;
using Messenger.Api.Configuration;
using Messenger.Api.Contracts;
using Messenger.Application.Interfaces;
using Messenger.Application.Options;
using Messenger.Application.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Messenger.Api.Authentication;

/// <summary>
/// Аутентификация по cookie-сессии: читает токен, ищет сессию в хранилище
/// и подставляет пользователя в HttpContext.User.
/// </summary>
public class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    ISessionStore sessionStore,
    IOptions<SessionCookieOptions> sessionOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, loggerFactory, encoder)
{
    public const string SchemeName = "Session";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var cookieName = sessionOptions.Value.CookieName;

        if (!Request.Cookies.TryGetValue(cookieName, out var token) || string.IsNullOrEmpty(token))
        {
            return AuthenticateResult.NoResult();
        }

        var session = await sessionStore.GetByTokenHashAsync(
            SessionTokenGenerator.Hash(token),
            Context.RequestAborted);

        if (session is null)
        {
            return AuthenticateResult.Fail("Сессия не найдена.");
        }

        if (session.ExpiresAt <= DateTime.UtcNow)
        {
            return AuthenticateResult.Fail("Сессия истекла.");
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, session.UserId.ToString()),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);

        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/problem+json";

        var problem = ApiProblem.Create(
            StatusCodes.Status401Unauthorized,
            "Требуется авторизация",
            "Сессия отсутствует или закончилась. Войдите заново.",
            ApiErrorCodes.Unauthorized);

        return Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: Context.RequestAborted);
    }
}