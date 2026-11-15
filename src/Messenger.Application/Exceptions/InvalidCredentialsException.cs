namespace Messenger.Application.Exceptions;

/// <summary>Неверные учётные данные. Одинаков для «нет такого пользователя» и «неверный пароль».</summary>
public class InvalidCredentialsException() : Exception("Неверный публичный ID или пароль.");
