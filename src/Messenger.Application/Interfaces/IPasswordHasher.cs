namespace Messenger.Application.Interfaces;

/// <summary>
/// Хеширование и проверка паролей. Реализация — Argon2id.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Возвращает хеш пароля в PHC-формате ($argon2id$...).</summary>
    string Hash(string password);

    /// <summary>Проверяет пароль против хеша. Параметры берутся из самого хеша.</summary>
    bool Verify(string password, string encodedHash);
}
