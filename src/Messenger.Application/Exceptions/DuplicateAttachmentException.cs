namespace Messenger.Application.Exceptions;

/// <summary>
/// Вложение с таким идентификатором уже загружено в эту копию сообщения.
/// Повторная загрузка того же идентификатора — конфликт.
/// </summary>
public class DuplicateAttachmentException()
    : Exception("Вложение с таким идентификатором уже загружено.");