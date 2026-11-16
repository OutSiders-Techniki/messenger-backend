using System.Security.Claims;
using Messenger.Api.Configuration;
using Messenger.Api.Contracts;
using Messenger.Application.DTOs;
using Messenger.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Messenger.Api.Controllers;

/// <summary>
/// Метаданные зашифрованных вложений. Сервер отдаёт только то, что знает:
/// размер, момент загрузки и зашифрованные клиентом метаданные. Исходное
/// имя файла и открытое содержимое серверу недоступны.
/// </summary>
[ApiController]
[Route("dialogs/{dialogId:guid}/messages/{clientMessageId:guid}/attachments")]
public class AttachmentsController(AttachmentService attachmentService) : ControllerBase
{
    /// <summary>
    /// Метаданные вложения. Доступно только владельцу копии сообщения,
    /// к которому вложение относится: постороннему вложение не
    /// раскрывается.
    /// </summary>
    [Authorize]
    [HttpGet("{clientAttachmentId:guid}")]
    [ResponseCache(NoStore = true)]
    [ProducesResponseType<AttachmentMetadataResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AttachmentMetadataResponse>> GetById(
        Guid dialogId,
        Guid clientMessageId,
        Guid clientAttachmentId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return BuildUnauthorized();
        }

        var metadata = await attachmentService.GetMetadataAsync(
            userId,
            dialogId,
            clientMessageId,
            clientAttachmentId,
            cancellationToken);

        return Ok(metadata);
    }

    /// <summary>
    /// Метаданные всех вложений копии сообщения. Список нужен клиенту,
    /// чтобы узнать о вложениях: сообщение зашифровано целиком, поэтому
    /// идентификаторы вложений он иначе не увидит.
    /// </summary>
    [Authorize]
    [HttpGet]
    [ResponseCache(NoStore = true)]
    [ProducesResponseType<IReadOnlyList<AttachmentMetadataResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<AttachmentMetadataResponse>>> List(
        Guid dialogId,
        Guid clientMessageId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return BuildUnauthorized();
        }

        var metadata = await attachmentService.ListAsync(
            userId,
            dialogId,
            clientMessageId,
            cancellationToken);

        return Ok(metadata);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(claim, out userId);
    }

    private static ObjectResult BuildUnauthorized() => new(ApiProblem.Create(
        StatusCodes.Status401Unauthorized,
        "Требуется авторизация",
        "Сессия отсутствует или закончилась. Войдите заново.",
        ApiErrorCodes.Unauthorized))
    {
        StatusCode = StatusCodes.Status401Unauthorized,
        ContentTypes = { "application/problem+json" },
    };
}