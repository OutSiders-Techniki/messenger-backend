namespace Messenger.Api.Contracts;

/// <summary>
/// Стабильные машинно-читаемые коды ошибок. Фронтенд ориентируется на
/// код, а не на текст сообщения: тексты на русском и могут меняться.
/// </summary>
public static class ApiErrorCodes
{
    /// <summary>Превышено число запросов за интервал.</summary>
    public const string RateLimited = "rate_limited";

    /// <summary>Идентификатор сообщения уже использован для другого пакета или отправителя.</summary>
    public const string MessageConflict = "message_conflict";

    /// <summary>Тело запроса не прошло проверку полей.</summary>
    public const string ValidationFailed = "validation_failed";

    /// <summary>Сессия отсутствует или закончилась.</summary>
    public const string Unauthorized = "unauthorized";

    /// <summary>Неверный публичный ID или пароль.</summary>
    public const string InvalidCredentials = "invalid_credentials";

    /// <summary>Пользователь с указанным публичным ID не найден.</summary>
    public const string UserNotFound = "user_not_found";

    /// <summary>Диалог не найден или пользователь в нём не участвует.</summary>
    public const string DialogNotFound = "dialog_not_found";

    /// <summary>Вложение не найдено или пользователь не имеет к нему доступа.</summary>
    public const string AttachmentNotFound = "attachment_not_found";

    /// <summary>Размер вложения превышает максимум 25 МБ.</summary>
    public const string AttachmentTooLarge = "attachment_too_large";

    /// <summary>Вложение с таким идентификатором уже загружено.</summary>
    public const string DuplicateAttachment = "duplicate_attachment";

    /// <summary>Запрошенный адрес не существует.</summary>
    public const string NotFound = "not_found";

    /// <summary>Метод не поддерживается для этого адреса.</summary>
    public const string MethodNotAllowed = "method_not_allowed";

    /// <summary>Доступ запрещён.</summary>
    public const string Forbidden = "forbidden";

    /// <summary>Неверный тип содержимого запроса.</summary>
    public const string UnsupportedMediaType = "unsupported_media_type";

    /// <summary>Диалог между этой парой участников уже существует.</summary>
    public const string DuplicateDialog = "duplicate_dialog";

    /// <summary>Публичный ID уже занят другим пользователем.</summary>
    public const string DuplicatePublicId = "duplicate_public_id";

    /// <summary>Внутренняя ошибка сервера.</summary>
    public const string InternalError = "internal_error";
}
