using System.Security.Cryptography;
using Messenger.Application.Interfaces;

namespace Messenger.Application.Services;

/// <summary>
/// Генерирует публичные ID из безопасного алфавита Crockford Base32
/// (без похожих символов 0/O, 1/I/L и буквы U) длиной 12 символов.
/// Использует криптографический ГСЧ без смещения по модулю.
/// </summary>
public class PublicIdGenerator : IPublicIdGenerator
{
    public const string Alphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";
    public const int IdLength = 12;

    public string Generate()
    {
        return string.Create(IdLength, Alphabet, static (span, alphabet) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
            }
        });
    }
}
