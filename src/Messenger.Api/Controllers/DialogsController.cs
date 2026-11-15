using System.Security.Claims;
using Messenger.Application.DTOs;
using Messenger.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Messenger.Api.Controllers;

[ApiController]
[Route("dialogs")]
public class DialogsController(DialogService dialogs) : ControllerBase
{
    /// <summary>
    /// Создаёт диалог один-на-один с указанным пользователем. Если такой
    /// диалог уже есть, возвращает его — дубль не создаётся.
    /// </summary>
    [Authorize]
    [HttpPost]
    [ProducesResponseType<DialogResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<DialogResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DialogResponse>> Create(
        [FromBody] CreateDialogRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userId, out var creatorId))
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Требуется авторизация",
                detail: "Сессия отсутствует или закончилась. Войдите заново.");
        }

        var result = await dialogs.CreateAsync(creatorId, request.PublicId!, cancellationToken);

        return result.Created
            ? StatusCode(StatusCodes.Status201Created, result.Dialog)
            : Ok(result.Dialog);
    }
}
