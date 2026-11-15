namespace Messenger.Application.Exceptions;

/// <summary>Пользователь с указанным публичным ID не найден.</summary>
public class UserNotFoundException()
    : Exception("Пользователь с указанным публичным ID не найден.");
