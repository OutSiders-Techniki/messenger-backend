using System.Security.Claims;
using Messenger.Api.Configuration;
using Messenger.Api.Contracts;
using Messenger.Application.DTOs;
using Messenger.Application.Exceptions;
using Messenger.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Messenger.Api.Controllers;

[ApiController]
[Authorize]
[Route("dialogs/{dialogId:guid}/messages")]
public sealed class MessagesController(MessageService messages) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost]
    [RequestSizeLimit(100 * 1024)]
    [ProducesResponseType<SendMessageResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<SendMessageResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SendMessageResponse>> Send(Guid dialogId, SendMessageRequest request, CancellationToken cancellationToken)
    {
        var result = await messages.SendAsync(UserId, dialogId, request, cancellationToken);
        return StatusCode(result.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK, result);
    }

    [HttpGet]
    [ProducesResponseType<MessageHistoryResponse>(StatusCodes.Status200OK)]
    public Task<MessageHistoryResponse> History(Guid dialogId, [FromQuery] int limit = 50, [FromQuery] string? before = null, CancellationToken cancellationToken = default) =>
        messages.GetHistoryAsync(UserId, dialogId, limit, before, cancellationToken);


}
