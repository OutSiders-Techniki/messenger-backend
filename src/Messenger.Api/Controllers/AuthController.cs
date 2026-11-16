using System.Security.Claims;
using Messenger.Api.Configuration;
using Messenger.Api.Contracts;
using Messenger.Application.DTOs;
using Messenger.Application.Options;
using Messenger.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Messenger.Api.Controllers;

[ApiController]
[Route("auth")]
public class AuthController(
    RegistrationService registrationService,
    AuthService authService,
    IOptions<SessionCookieOptions> sessionOptions) : ControllerBase
{
    /// <summary>
    /// Регистрация нового аккаунта. Телефон и email не требуются —
    /// сервер выдаёт публичный ID, по которому пользователя можно найти.
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType<RegisterResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegisterResponse>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var response = await registrationService.RegisterAsync(request, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>Вход по публичному ID и паролю. Устанавливает cookie-сессию.</summary>
    [HttpPost("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var (token, publicId) = await authService.LoginAsync(request, cancellationToken);

        SetSessionCookie(token);

        return Ok(new LoginResponse(publicId));
    }

    /// <summary>Выход: отзывает сессию на сервере и удаляет cookie.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var token = ReadSessionToken();

        if (!string.IsNullOrEmpty(token))
        {
            await authService.LogoutAsync(token, cancellationToken);
        }

        DeleteSessionCookie();

        return NoContent();
    }

    /// <summary>Текущий пользователь. Требует действующей сессии.</summary>
    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentUserResponse>> CurrentUser(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out var id))
        {
            return BuildUnauthorized();
        }

        var user = await authService.GetCurrentUserAsync(id, cancellationToken);

        if (user is null)
        {
            return BuildUnauthorized();
        }

        return Ok(user);
    }

    private ObjectResult BuildUnauthorized() => new(ApiProblem.Create(
        StatusCodes.Status401Unauthorized,
        "Требуется авторизация",
        "Сессия отсутствует или закончилась. Войдите заново.",
        ApiErrorCodes.Unauthorized))
    {
        StatusCode = StatusCodes.Status401Unauthorized,
        ContentTypes = { "application/problem+json" },
    };

    private string? ReadSessionToken()
    {
        return Request.Cookies.TryGetValue(sessionOptions.Value.CookieName, out var token)
            ? token
            : null;
    }

    private void SetSessionCookie(string token)
    {
        Response.Cookies.Append(sessionOptions.Value.CookieName, token, BuildCookieOptions());
    }

    private void DeleteSessionCookie()
    {
        Response.Cookies.Delete(sessionOptions.Value.CookieName, BuildCookieOptions());
    }

    private CookieOptions BuildCookieOptions() => new()
    {
        HttpOnly = true,
        Secure = sessionOptions.Value.Secure,
        SameSite = ParseSameSite(sessionOptions.Value.SameSite),
        Path = "/",
        IsEssential = true,

        // Cookie живёт столько же, сколько серверная сессия.
        Expires = DateTimeOffset.UtcNow.AddHours(sessionOptions.Value.LifetimeHours),
    };

    private static SameSiteMode ParseSameSite(string value) =>
        value.ToLowerInvariant() switch
        {
            "lax" => SameSiteMode.Lax,
            "none" => SameSiteMode.None,
            _ => SameSiteMode.Strict,
        };
}