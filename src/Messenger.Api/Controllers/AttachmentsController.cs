using System.Security.Claims;
using Messenger.Api.Configuration;
using Messenger.Api.Contracts;
using Messenger.Api.OpenApi;
using Messenger.Application;
using Messenger.Application.DTOs;
using Messenger.Application.Exceptions;
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
    /// Зашифрованные клиентом метаданные вложения: имя файла и тип
    /// содержимого. Передаются в base64, чтобы не зависеть от кодировки
    /// HTTP-заголовков.
    /// </summary>
    private const string MetadataHeaderName = "X-Attachment-Metadata";

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

    /// <summary>
    /// Загрузка зашифрованных байтов вложения в свою копию сообщения.
    /// Тело запроса — сами зашифрованные байты, без имени файла и типа
    /// содержимого: MIME клиента не считается доверенным и сервер его не
    /// использует. Зашифрованные клиентом метаданные (имя файла, тип)
    /// передаются в заголовке X-Attachment-Metadata в base64.
    /// </summary>
    [Authorize]
    [AttachmentUploadBody]
    [HttpPost("{clientAttachmentId:guid}")]
    [ProducesResponseType<AttachmentMetadataResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AttachmentMetadataResponse>> Upload(
        Guid dialogId,
        Guid clientMessageId,
        Guid clientAttachmentId,
        [FromHeader(Name = MetadataHeaderName)]
        string? encryptedMetadata,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return BuildUnauthorized();
        }

        var metadata = DecodeMetadata(encryptedMetadata);

        // Размер известен заранее — отклоняем до чтения тела.
        if (Request.ContentLength is > 0 and > MediaLimits.MaxAttachmentSizeBytes)
        {
            throw new AttachmentTooLargeException(MediaLimits.MaxAttachmentSizeBytes);
        }

        var uploaded = await attachmentService.UploadAsync(
            userId,
            dialogId,
            clientMessageId,
            clientAttachmentId,
            metadata,
            Request.Body,
            cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { dialogId, clientMessageId, clientAttachmentId },
            uploaded);
    }

    /// <summary>
    /// Скачивание зашифрованных байтов вложения из своей копии сообщения.
    /// Тело ответа — сами зашифрованные байты: расшифровка выполняется на
    /// клиенте ключом диалога. Доступно только владельцу копии сообщения.
    /// </summary>
    [Authorize]
    [HttpGet("{clientAttachmentId:guid}/content")]
    [ResponseCache(NoStore = true)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Download(
        Guid dialogId,
        Guid clientMessageId,
        Guid clientAttachmentId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return BuildUnauthorized();
        }

        var content = await attachmentService.OpenContentAsync(
            userId,
            dialogId,
            clientMessageId,
            clientAttachmentId,
            cancellationToken);

        return File(content, "application/octet-stream", enableRangeProcessing: false);
    }

    private static byte[] DecodeMetadata(string? encryptedMetadata)
    {
        if (string.IsNullOrWhiteSpace(encryptedMetadata))
        {
            throw new ValidationException("Зашифрованные метаданные вложения обязательны.");
        }

        try
        {
            return Convert.FromBase64String(encryptedMetadata);
        }
        catch (FormatException)
        {
            throw new ValidationException(
                "Зашифрованные метаданные вложения должны быть в формате base64.");
        }
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