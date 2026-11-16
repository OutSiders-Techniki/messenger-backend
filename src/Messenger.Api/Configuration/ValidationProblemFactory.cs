using Messenger.Api.Contracts;
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
            Type = ApiProblem.TypeBase + "1",
        };

        problem.Extensions["code"] = ApiErrorCodes.ValidationFailed;

        return new BadRequestObjectResult(problem)
        {
            ContentTypes = { "application/problem+json" },
        };
    }
}