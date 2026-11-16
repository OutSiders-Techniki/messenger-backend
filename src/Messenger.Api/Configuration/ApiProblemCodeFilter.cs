using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Messenger.Api.Configuration;

/// <summary>
/// Ошибки, которые MVC формирует сам (например, 415 при неверном
/// Content-Type), приводятся к тому же виду, что и остальные ответы:
/// русский текст и машинно-читаемый код.
/// </summary>
public sealed class ApiProblemCodeFilter : IAsyncResultFilter
{
    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult { Value: ProblemDetails problem } objectResult)
        {
            // Свои ошибки уже собраны правильно — не трогаем.
            if (!problem.Extensions.ContainsKey("code"))
            {
                var status = objectResult.StatusCode
                             ?? problem.Status
                             ?? StatusCodes.Status500InternalServerError;

                var (title, detail, code) = ApiProblem.ForStatus(status);

                problem.Status = status;
                problem.Type = ApiProblem.TypeFor(status);
                problem.Title = title;
                problem.Detail = detail;

                problem.Extensions["code"] = code;
            }

            objectResult.ContentTypes.Clear();
            objectResult.ContentTypes.Add("application/problem+json");
        }

        return next();
    }
}