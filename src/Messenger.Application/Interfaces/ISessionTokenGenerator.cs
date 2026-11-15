namespace Messenger.Application.Interfaces;

/// <summary>Генерирует токен сессии и его хеш для хранения на сервере.</summary>
public interface ISessionTokenGenerator
{
    SessionToken Create();
}

/// <param name="Value">Значение, которое уходит клиенту в cookie.</param>
/// <param name="Hash">Хеш значения, который хранится в БД.</param>
public sealed record SessionToken(string Value, string Hash);
