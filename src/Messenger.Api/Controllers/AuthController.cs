using Messenger.Application.DTOs;
using Messenger.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Messenger.Api.Controllers;

[ApiController]
[Route("auth")]
public class AuthController(RegistrationService registrationService) : ControllerBase
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
}
