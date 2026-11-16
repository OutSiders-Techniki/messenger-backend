using System.Text.Json.Nodes;
using Messenger.Api.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Messenger.Api.OpenApi;

/// <summary>
/// Оформление документа спецификации API: заголовок, версия, схема
/// авторизации по cookie-сессии и машинно-читаемый код ошибки в схеме
/// ProblemDetails.
/// </summary>
public static class ApiOpenApiConfiguration
{
    /// <summary>Имя схемы безопасности в документе спецификации.</summary>
    public const string SessionSchemeName = "sessionCookie";

    /// <summary>Имя документа спецификации.</summary>
    public const string DocumentName = "v1";

    /// <summary>Все коды ошибок, которые сервер может вернуть.</summary>
    public static IReadOnlyList<string> KnownErrorCodes { get; } =
    [
        ApiErrorCodes.ValidationFailed,
        ApiErrorCodes.Unauthorized,
        ApiErrorCodes.InvalidCredentials,
        ApiErrorCodes.UserNotFound,
        ApiErrorCodes.DialogNotFound,
        ApiErrorCodes.NotFound,
        ApiErrorCodes.MethodNotAllowed,
        ApiErrorCodes.Forbidden,
        ApiErrorCodes.UnsupportedMediaType,
        ApiErrorCodes.DuplicateDialog,
        ApiErrorCodes.DuplicatePublicId,
        ApiErrorCodes.InternalError,
    ];

    /// <summary>
    /// Добавляет общие сведения о сервисе, схему авторизации и свойство
    /// code в схеме ошибки.
    /// </summary>
    public static void Configure(OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = "Мессенджер API",
                Version = DocumentName,
                Description =
                    "REST API мессенджера со сквозным шифрованием. "
                    + "Ошибки возвращаются в формате RFC 9457 (ProblemDetails) "
                    + "с полем code — стабильным машинно-читаемым кодом ошибки.",
            };

            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

            document.Components.SecuritySchemes[SessionSchemeName] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Cookie,
                Name = "messenger_session",
                Description = "Сессия, выданная при входе. Передаётся в cookie.",
            };

            return Task.CompletedTask;
        });

        options.AddSchemaTransformer((schema, context, _) =>
        {
            // В схеме ошибки показываем машинно-читаемый код: фронтенд
            // ориентируется на него, а не на текст сообщения.
            if (context.JsonTypeInfo?.Type == typeof(ProblemDetails))
            {
                schema.Properties ??= new Dictionary<string, IOpenApiSchema>();

                schema.Properties["code"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Description = "Стабильный код ошибки для программной обработки.",
                    Enum = KnownErrorCodes
                        .Select(code => (JsonNode)JsonValue.Create(code))
                        .ToList(),
                };

                // Ошибки проверки полей: имя поля → список сообщений.
                schema.Properties["errors"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    Description =
                        "Сообщения по полям запроса. Заполняется только при ошибке валидации.",
                    AdditionalProperties = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Array,
                        Items = new OpenApiSchema { Type = JsonSchemaType.String },
                    },
                };
            }

            return Task.CompletedTask;
        });

        options.AddOperationTransformer((operation, context, _) =>
        {
            // Методы, требующие сессии, помечаем схемой авторизации.
            var requiresAuthorization = RequiresAuthorization(context);

            if (requiresAuthorization)
            {
                operation.Security ??= new List<OpenApiSecurityRequirement>();

                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(SessionSchemeName, context.Document)]
                        = new List<string>(),
                });
            }

            // Ошибки описываем по-русски, с указанием кода и в том же
            // media type, что отдаёт сервер.
            if (operation.Responses is not null)
            {
                foreach (var (statusCode, response) in operation.Responses)
                {
                    if (!int.TryParse(statusCode, out var status))
                    {
                        continue;
                    }

                    if (status < 400)
                    {
                        // Успешный ответ отдаётся в application/json.
                        KeepOnlyMediaType(response, "application/json");

                        continue;
                    }

                    response.Description = ErrorDescriptionFor(status);

                    // Ошибки отдаются в problem+json, а не в общем списке
                    // media type — кодогенератору нужен точный тип.
                    KeepOnlyMediaType(response, "application/problem+json");
                }

                AddMissingErrorResponses(operation, context, requiresAuthorization);
            }

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Добавляет ответы об ошибках, которые сервер может вернуть всегда:
    /// неверный метод для адреса, внутренняя ошибка и — для защищённых
    /// методов — отсутствие сессии.
    /// </summary>
    private static void AddMissingErrorResponses(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        bool requiresAuthorization)
    {
        operation.Responses ??= new OpenApiResponses();

        var schema = new OpenApiSchemaReference("ProblemDetails", context.Document);

        void Ensure(int status, string description)
        {
            var key = status.ToString();

            if (operation.Responses!.ContainsKey(key))
            {
                return;
            }

            operation.Responses[key] = new OpenApiResponse
            {
                Description = description,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/problem+json"] = new OpenApiMediaType { Schema = schema },
                },
            };
        }

        Ensure(StatusCodes.Status405MethodNotAllowed, ErrorDescriptionFor(405));
        Ensure(StatusCodes.Status500InternalServerError, ErrorDescriptionFor(500));

        // Неверный тип содержимого возможен только у запросов с телом.
        if (operation.RequestBody is not null)
        {
            Ensure(StatusCodes.Status415UnsupportedMediaType, ErrorDescriptionFor(415));
        }

        if (requiresAuthorization)
        {
            Ensure(StatusCodes.Status401Unauthorized, ErrorDescriptionFor(401));
        }
    }

    /// <summary>
    /// Оставляет в ответе только указанный media type: по умолчанию ASP.NET
    /// перечисляет text/plain, application/json и text/json, что мешает
    /// генераторам клиентов.
    /// </summary>
    private static void KeepOnlyMediaType(IOpenApiResponse response, string mediaType)
    {
        if (response.Content is null)
        {
            return;
        }

        var schema = response.Content
            .FirstOrDefault(entry => entry.Key is "application/json" or "text/plain")
            .Value?.Schema;

        // Ответ без тела (например, 204) — media type указывать не на что.
        if (schema is null)
        {
            response.Content.Clear();

            return;
        }

        response.Content.Clear();

        response.Content[mediaType] = new OpenApiMediaType
        {
            Schema = schema,
        };
    }

    /// <summary>Тексты ответов об ошибках: коды перечислены явно.</summary>
    private static string ErrorDescriptionFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest =>
            $"Ошибка валидации запроса. Код: {ApiErrorCodes.ValidationFailed}.",
        StatusCodes.Status401Unauthorized =>
            $"Сессия отсутствует или закончилась, либо неверные учётные данные. "
            + $"Коды: {ApiErrorCodes.Unauthorized}, {ApiErrorCodes.InvalidCredentials}.",
        StatusCodes.Status403Forbidden =>
            $"Доступ запрещён. Код: {ApiErrorCodes.Forbidden}.",
        StatusCodes.Status404NotFound =>
            $"Объект не найден или пользователь не имеет к нему доступа. "
            + $"Коды: {ApiErrorCodes.UserNotFound}, {ApiErrorCodes.DialogNotFound}, {ApiErrorCodes.NotFound}.",
        StatusCodes.Status409Conflict =>
            $"Объект уже существует. Коды: {ApiErrorCodes.DuplicateDialog}, {ApiErrorCodes.DuplicatePublicId}.",
        StatusCodes.Status405MethodNotAllowed =>
            $"Метод не поддерживается для этого адреса. Код: {ApiErrorCodes.MethodNotAllowed}.",
        StatusCodes.Status415UnsupportedMediaType =>
            $"Неверный тип содержимого запроса. Код: {ApiErrorCodes.UnsupportedMediaType}.",
        _ =>
            $"Внутренняя ошибка сервера. Код: {ApiErrorCodes.InternalError}.",
    };

    private static bool RequiresAuthorization(OpenApiOperationTransformerContext context)
    {
        var metadata = context.Description?.ActionDescriptor?.EndpointMetadata;

        if (metadata is null)
        {
            return false;
        }

        foreach (var item in metadata)
        {
            if (item is IAuthorizeData)
            {
                return true;
            }
        }

        return false;
    }
}