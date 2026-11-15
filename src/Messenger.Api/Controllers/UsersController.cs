using Messenger.Application.DTOs;
using Messenger.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Messenger.Api.Controllers;

[ApiController]
[Route("users")]
public class UsersController(UserSearchService userSearch) : ControllerBase
{
    /// <summary>
    /// Точный поиск пользователя по публичному ID. Частичный ID совпадений
    /// не даёт — ищем только полное совпадение.
    /// </summary>
    [Authorize]
    [HttpGet("by-id/{id}")]
    [ResponseCache(NoStore = true)]
    [ProducesResponseType<UserSearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserSearchResponse>> ById(
        string id,
        CancellationToken cancellationToken)
    {
        var response = await userSearch.FindByPublicIdAsync(id, cancellationToken);

        return Ok(response);
    }
}
