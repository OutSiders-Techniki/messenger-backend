using Isopoh.Cryptography.Argon2;
using Messenger.Application.Interfaces;

namespace Messenger.Infrastructure.Services;

/// <summary>
/// Argon2id с параметрами OWASP: 64 МиБ памяти, 3 итерации, 1 поток.
/// Хеш хранится в PHC-формате ($argon2id$v=19$m=65536,t=3,p=1$...),
/// поэтому параметры извлекаются из самой строки при проверке.
/// </summary>
public class Argon2IdPasswordHasher : IPasswordHasher
{
    private const int TimeCost = 3;
    private const int MemoryCost = 65536;
    private const int Parallelism = 1;
    private const int HashLength = 32;

    public string Hash(string password)
    {
        return Argon2.Hash(
            password,
            timeCost: TimeCost,
            memoryCost: MemoryCost,
            parallelism: Parallelism,
            type: Argon2Type.HybridAddressing,
            hashLength: HashLength);
    }

    public bool Verify(string password, string encodedHash)
    {
        return Argon2.Verify(encodedHash, password);
    }
}
