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


}
