using System.Security.Claims;
using Messenger.Application.DTOs;
using Messenger.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Messenger.Api.Controllers;

[ApiController]
[Authorize]
[Route("dialogs/{dialogId:guid}/messages")]
public sealed class MessageBatchDeletionController(MessageDeletionService messages) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Удаление выбранных сообщений у себя; {} очищает всю личную историю.</summary>
    [HttpPost("delete-self")]
    [RequestSizeLimit(32 * 1024)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSelf(
        Guid dialogId, [FromBody] DeleteSelfMessagesRequest request, CancellationToken cancellationToken)
    {
        await messages.DeleteSelfAsync(UserId, dialogId, request, cancellationToken);
        return NoContent();
    }

    /// <summary>Атомарное удаление выбранных сообщений у обоих участников.</summary>
    [HttpPost("delete-both")]
    [RequestSizeLimit(32 * 1024)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteBoth(
        Guid dialogId, [FromBody] DeleteBothMessagesRequest request, CancellationToken cancellationToken)
    {
        await messages.DeleteBothAsync(UserId, dialogId, request, cancellationToken);
        return NoContent();
    }
}
