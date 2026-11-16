using Messenger.Api.Configuration;
using Messenger.Api.Contracts;
using Messenger.Application.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace Messenger.Api.Middleware;

/// <summary>
/// Преобразует исключения приложения в ProblemDetails (RFC 9457),
/// чтобы клиент получал единообразный формат ошибок.
/// </summary>
public class ApplicationExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApplicationExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title, detail, code) = exception switch
        {
            ValidationException =>
                (StatusCodes.Status400BadRequest, "Ошибка валидации", exception.Message,
                    ApiErrorCodes.ValidationFailed),
            InvalidCredentialsException =>
                (StatusCodes.Status401Unauthorized, "Неверные учётные данные", exception.Message,
                    ApiErrorCodes.InvalidCredentials),
            UserNotFoundException =>
                (StatusCodes.Status404NotFound, "Пользователь не найден", exception.Message,
                    ApiErrorCodes.UserNotFound),
            DialogNotFoundException =>
                (StatusCodes.Status404NotFound, "Диалог не найден", exception.Message,
                    ApiErrorCodes.DialogNotFound),
            AttachmentNotFoundException =>
                (StatusCodes.Status404NotFound, "Вложение не найдено", exception.Message,
                    ApiErrorCodes.AttachmentNotFound),
            DuplicateDialogException =>
                (StatusCodes.Status409Conflict, "Диалог уже существует", exception.Message,
                    ApiErrorCodes.DuplicateDialog),
            DuplicatePublicIdException =>
                (StatusCodes.Status409Conflict, "Публичный ID уже занят", exception.Message,
                    ApiErrorCodes.DuplicatePublicId),
            _ =>
                (StatusCodes.Status500InternalServerError, "Внутренняя ошибка сервера",
                    "Внутренняя ошибка сервера. Повторите попытку позже.",
                    ApiErrorCodes.InternalError),
        };

        if (status >= StatusCodes.Status500InternalServerError)
        {
            // Детали известных ошибок безопасны, но текст непредвиденного
            // исключения клиенту не отдаём — только в лог.
            logger.LogError(
                exception,
                "Unhandled exception while processing {Method} {Path}",
                httpContext.Request.Method,
                httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = ApiProblem.Create(status, title, detail, code),
        });
    }
}