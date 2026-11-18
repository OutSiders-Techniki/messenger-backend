namespace Messenger.Application.Exceptions;

public sealed class MessageConflictException() : Exception("Идентификатор сообщения уже использован другим пакетом.");
