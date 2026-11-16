namespace Messenger.Application.Exceptions;

/// <summary>
/// Диалог не найден или пользователь в нём не участвует. Ответ одинаковый:
/// постороннему не сообщаем, что диалог существует.
/// </summary>
public class DialogNotFoundException()
    : Exception("Диалог не найден.");