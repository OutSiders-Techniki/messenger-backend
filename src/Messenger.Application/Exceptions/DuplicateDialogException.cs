namespace Messenger.Application.Exceptions;

/// <summary>Диалог между этой парой участников уже существует.</summary>
public class DuplicateDialogException()
    : Exception("Диалог между этими пользователями уже существует.");
