namespace Messenger.Application.Interfaces;

/// <summary>
/// Генератор публичного ID — читаемого идентификатора аккаунта,
/// которым пользователь делится для поиска.
/// </summary>
public interface IPublicIdGenerator
{
    string Generate();
}
