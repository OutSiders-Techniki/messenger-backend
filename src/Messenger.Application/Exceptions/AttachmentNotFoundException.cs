namespace Messenger.Application.Exceptions;

/// <summary>
/// Вложение не найдено или пользователь не имеет к нему доступа. Ответ
/// одинаковый: постороннему не сообщаем, что вложение существует.
/// </summary>
public class AttachmentNotFoundException()
    : Exception("Вложение не найдено.");