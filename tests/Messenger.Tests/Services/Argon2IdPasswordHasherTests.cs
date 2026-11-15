using Messenger.Infrastructure.Services;

namespace Messenger.Tests.Services;

public class Argon2IdPasswordHasherTests
{
    private readonly Argon2IdPasswordHasher _sut = new();

    [Fact]
    public void Hash_DoesNotContainPlainPassword()
    {
        const string password = "correct horse battery staple";

        var hash = _sut.Hash(password);

        Assert.DoesNotContain(password, hash);
    }

    [Fact]
    public void Hash_UsesArgon2idPhcFormat()
    {
        var hash = _sut.Hash("password123");

        Assert.StartsWith("$argon2id$", hash);
    }

    [Fact]
    public void Hash_ProducesDifferentHashesForSamePassword()
    {
        // Соль случайная — одинаковые пароли дают разные хеши
        var first = _sut.Hash("password123");
        var second = _sut.Hash("password123");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Verify_AcceptsCorrectPassword()
    {
        var hash = _sut.Hash("password123");

        Assert.True(_sut.Verify("password123", hash));
    }

    [Fact]
    public void Verify_RejectsWrongPassword()
    {
        var hash = _sut.Hash("password123");

        Assert.False(_sut.Verify("password124", hash));
    }

    [Fact]
    public void Verify_RejectsMalformedHash()
    {
        Assert.False(_sut.Verify("password123", "not-a-hash"));
    }
}
