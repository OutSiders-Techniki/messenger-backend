using Messenger.Api.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Messenger.Api.Configuration;

/// <summary>
/// Сборка ответов об ошибках в одном формате RFC 9457 (ProblemDetails)
/// с машинно-читаемым кодом. Фронтенд ориентируется на код, а не на
/// текст: тексты на русском и со временем меняются.
/// </summary>
public static class ApiProblem
{
    /// <summary>Ссылка на описание класса ошибки в RFC 9110.</summary>
    public const string TypeBase = "https://tools.ietf.org/html/rfc9110#section-15.5.";

    /// <summary>Ссылка на описание класса ошибки по коду ответа.</summary>
    public static string TypeFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => TypeBase + "1",
        StatusCodes.Status401Unauthorized => TypeBase + "2",
        StatusCodes.Status403Forbidden => TypeBase + "4",
        StatusCodes.Status404NotFound => TypeBase + "5",
        StatusCodes.Status405MethodNotAllowed => TypeBase + "6",
        StatusCodes.Status409Conflict => TypeBase + "10",
        StatusCodes.Status415UnsupportedMediaType => TypeBase + "16",
        _ => "https://tools.ietf.org/html/rfc9110#section-15.6.1",
    };

    /// <summary>
    /// Заголовок, подробности и код ошибки по коду ответа. Единый
    /// источник текстов: их используют и обработчик исключений, и
    /// middleware, и фильтр результатов MVC.
    /// </summary>
    public static (string Title, string Detail, string Code) ForStatus(int status) => status switch
    {
        StatusCodes.Status400BadRequest =>
            ("Ошибка валидации", "Проверьте правильность переданных данных.",
                ApiErrorCodes.ValidationFailed),
        StatusCodes.Status401Unauthorized =>
            ("Требуется авторизация", "Сессия отсутствует или закончилась. Войдите заново.",
                ApiErrorCodes.Unauthorized),
        StatusCodes.Status403Forbidden =>
            ("Доступ запрещён", "Недостаточно прав для этого действия.", ApiErrorCodes.Forbidden),
        StatusCodes.Status404NotFound =>
            ("Ресурс не найден", "Запрошенный адрес не существует.", ApiErrorCodes.NotFound),
        StatusCodes.Status405MethodNotAllowed =>
            ("Метод не поддерживается", "Для этого адреса такой метод не поддерживается.",
                ApiErrorCodes.MethodNotAllowed),
        StatusCodes.Status409Conflict =>
            ("Конфликт", "Объект уже существует.", ApiErrorCodes.DuplicateDialog),
        StatusCodes.Status415UnsupportedMediaType =>
            ("Неверный тип содержимого", "Ожидается запрос в формате JSON.",
                ApiErrorCodes.UnsupportedMediaType),
        _ when status >= StatusCodes.Status500InternalServerError =>
            ("Внутренняя ошибка сервера", "Внутренняя ошибка сервера. Повторите попытку позже.",
                ApiErrorCodes.InternalError),
        _ =>
            ("Ошибка запроса", "Запрос не удалось обработать.", ApiErrorCodes.ValidationFailed),
    };

    /// <summary>
    /// Ответ об ошибке: код, заголовок и подробности.
    /// </summary>
    public static ProblemDetails Create(
        int status,
        string title,
        string detail,
        string code,
        string? type = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = type ?? TypeFor(status),
        };

        problem.Extensions["code"] = code;

        return problem;
    }
}