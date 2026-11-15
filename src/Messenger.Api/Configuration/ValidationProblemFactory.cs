using Microsoft.AspNetCore.Mvc;

namespace Messenger.Api.Configuration;

/// <summary>
/// Стандартный ответ AspNetCore на ошибку валидации с английским
/// заголовком заменяем на русский. Тексты самих полей заданы
/// атрибутами в DTO, поэтому здесь они не переписываются.
/// </summary>
public static class ValidationProblemFactory
{
    public static IActionResult Create(ActionContext context)
    {
        var problem = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Ошибка валидации",
            Detail = "Проверьте правильность переданных данных.",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        };

        return new BadRequestObjectResult(problem)
        {
            ContentTypes = { "application/problem+json" },
        };
    }
}
