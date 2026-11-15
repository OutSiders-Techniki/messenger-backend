using Messenger.Application.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

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
        var (status, title, detail) = exception switch
        {
            ValidationException =>
                (StatusCodes.Status400BadRequest, "Ошибка валидации", exception.Message),
            InvalidCredentialsException =>
                (StatusCodes.Status401Unauthorized, "Неверные учётные данные", exception.Message),
            DuplicatePublicIdException =>
                (StatusCodes.Status409Conflict, "Публичный ID уже занят", exception.Message),
            _ =>
                (StatusCodes.Status500InternalServerError, "Внутренняя ошибка сервера",
                    "Внутренняя ошибка сервера. Повторите попытку позже."),
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
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail,
            },
        });
    }
}
