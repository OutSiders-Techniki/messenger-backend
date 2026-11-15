using System.Security.Cryptography;
using System.Text;
using Messenger.Application.Interfaces;

namespace Messenger.Application.Services;

/// <summary>
/// Токен — 32 случайных байта в base64url. На сервере хранится только
/// SHA-256 хеш строки токена, поэтому утечка БД не даёт готовых сессий.
///
/// Хеш считается именно по строке токена: значение приходит из cookie и
/// полностью контролируется клиентом, поэтому декодировать его нельзя —
/// некорректная строка просто не найдёт сессию, а не уронит приложение.
/// </summary>
public class SessionTokenGenerator : ISessionTokenGenerator
{
    private const int TokenByteLength = 32;

    public SessionToken Create()
    {
        var token = ToBase64Url(RandomNumberGenerator.GetBytes(TokenByteLength));

        return new SessionToken(token, Hash(token));
    }

    /// <summary>Хеш выданного токена — для отзыва сессии и поиска.</summary>
    public static string Hash(string token) =>
        ToBase64Url(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
