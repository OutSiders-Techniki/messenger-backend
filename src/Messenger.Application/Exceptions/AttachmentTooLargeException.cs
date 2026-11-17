namespace Messenger.Application.Exceptions;

/// <summary>
/// Размер вложения превышает максимум (25 МБ). Клиенту возвращается
/// понятное сообщение с указанием лимита.
/// </summary>
public class AttachmentTooLargeException(long maxBytes)
    : Exception($"Максимальный размер одного вложения — {maxBytes / 1_000_000} МБ.");