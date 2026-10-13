using PropFlow.Shared;

namespace PropFlow.UnitTests;

public sealed class IdentifierCodeNormalizerTests
{
    [Theory]
    [InlineData("Hồ bơi", "HOBOI")]
    [InlineData("Khu A - tầng 5", "KHUATANG5")]
    [InlineData("Đèn hành lang", "DENHANHLANG")]
    public void NormalizeRequired_RemovesVietnameseDiacriticsAndUnsafeCharacters(string source, string expected)
    {
        Assert.Equal(expected, IdentifierCodeNormalizer.NormalizeRequired(source, "Tên"));
    }

    [Fact]
    public void NormalizeRequired_RejectsValueWithoutIdentifierCharacters()
    {
        Assert.Throws<ArgumentException>(() => IdentifierCodeNormalizer.NormalizeRequired("---", "Tên"));
    }

    [Fact]
    public void AdvisoryLockKey_IsStableForSameScope()
    {
        Assert.Equal(IdentifierCodeNormalizer.AdvisoryLockKey("equipment:MAYBOM"), IdentifierCodeNormalizer.AdvisoryLockKey("equipment:MAYBOM"));
    }
}
