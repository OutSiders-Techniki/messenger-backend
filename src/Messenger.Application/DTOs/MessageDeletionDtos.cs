namespace Messenger.Application.DTOs;

/// <summary>Отсутствие messageIds удаляет всю личную копию переписки; пустой массив ничего не удаляет.</summary>
public sealed record DeleteSelfMessagesRequest(Guid[]? MessageIds = null);

/// <summary>Явный список сообщений для удаления у обоих участников.</summary>
public sealed record DeleteBothMessagesRequest(Guid[]? MessageIds);
