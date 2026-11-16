using System.Security.Claims;
using Messenger.Application.DTOs;
using Messenger.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Messenger.Api.Controllers;

[ApiController]
[Route("dialogs")]
public class DialogsController(DialogService dialogService) : ControllerBase
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
        if (!TryGetUserId(out var creatorId))
        {
            return BuildUnauthorized();
        }

        var result = await dialogService.CreateAsync(creatorId, request.PublicId!, cancellationToken);

        return result.Created
            ? StatusCode(StatusCodes.Status201Created, result.Dialog)
            : Ok(result.Dialog);
    }

    /// <summary>
    /// Список диалогов пользователя. Диалоги, в которых пользователь не
    /// участвует, не показываются.
    /// </summary>
    [Authorize]
    [HttpGet]
    [ResponseCache(NoStore = true)]
    [ProducesResponseType<IReadOnlyList<DialogListItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<DialogListItem>>> List(
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return BuildUnauthorized();
        }

        var dialogs = await dialogService.GetListAsync(userId, cancellationToken);

        return Ok(dialogs);
    }

    /// <summary>
    /// Открытие диалога. Доступно только участникам: чужой диалог по
    /// идентификатору не раскрывается.
    /// </summary>
    [Authorize]
    [HttpGet("{id:guid}")]
    [ResponseCache(NoStore = true)]
    [ProducesResponseType<DialogListItem>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<DialogListItem>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return BuildUnauthorized();
        }

        var dialog = await dialogService.GetByIdAsync(userId, id, cancellationToken);

        return Ok(dialog);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(claim, out userId);
    }

    private ObjectResult BuildUnauthorized() => new(new ProblemDetails
    {
        Status = StatusCodes.Status401Unauthorized,
        Title = "Требуется авторизация",
        Detail = "Сессия отсутствует или закончилась. Войдите заново.",
    })
    {
        StatusCode = StatusCodes.Status401Unauthorized,
        ContentTypes = { "application/problem+json" },
    };
}