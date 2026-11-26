using ApiKeyVault.Core.Model;

namespace ApiKeyVault.Core.Tests;

public class SecretMaskerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("short")]
    [InlineData("12345678901")] // 11 chars: only 7 in between
    public void MaskWithEnds_WhenTooShort_HidesAllCharacters(string? secret)
    {
        Assert.Equal(SecretMasker.FullMask, SecretMasker.MaskWithEnds(secret));
    }

    [Theory]
    [InlineData("ab12345678yz", "ab••••••••yz")] // 12 chars: exact boundary (2 + 8 + 2)
    [InlineData("secret-value-123", "se••••••••23")]
    [InlineData("sk-proj-abcdef1234567890xy", "sk••••••••xy")]
    public void MaskWithEnds_WhenAtLeastEightMiddleChars_ShowsFirstTwoAndLastTwo(string secret, string expected)
    {
        Assert.Equal(expected, SecretMasker.MaskWithEnds(secret));
    }
}
