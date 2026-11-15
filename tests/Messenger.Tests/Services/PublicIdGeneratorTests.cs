using Messenger.Application.Services;

namespace Messenger.Tests.Services;

public class PublicIdGeneratorTests
{
    private readonly PublicIdGenerator _sut = new();

    [Fact]
    public void Generate_ReturnsIdOfExpectedLength()
    {
        var id = _sut.Generate();

        Assert.Equal(PublicIdGenerator.IdLength, id.Length);
    }

    [Fact]
    public void Generate_UsesOnlySafeAlphabet()
    {
        for (var i = 0; i < 1000; i++)
        {
            var id = _sut.Generate();

            Assert.All(id, c => Assert.Contains(c, PublicIdGenerator.Alphabet));
        }
    }

    [Fact]
    public void Generate_DoesNotContainAmbiguousCharacters()
    {
        // Похожие символы запрещены: 0/O, 1/I/L, а также U
        var forbidden = new[] { '0', 'O', '1', 'I', 'L', 'U' };

        for (var i = 0; i < 1000; i++)
        {
            var id = _sut.Generate();

            Assert.All(id, c => Assert.DoesNotContain(c, forbidden));
        }
    }

    [Fact]
    public void Generate_ProducesUniqueIds()
    {
        const int count = 100_000;

        var ids = new HashSet<string>();
        for (var i = 0; i < count; i++)
        {
            ids.Add(_sut.Generate());
        }

        Assert.Equal(count, ids.Count);
    }
}
