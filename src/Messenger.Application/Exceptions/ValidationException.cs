namespace Messenger.Application.Exceptions;

/// <summary>Входные данные не прошли валидацию.</summary>
public class ValidationException(string message) : Exception(message);
