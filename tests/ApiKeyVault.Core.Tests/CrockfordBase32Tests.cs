using ApiKeyVault.Core.Cryptography;

namespace ApiKeyVault.Core.Tests;

public class CrockfordBase32Tests
{
    [Fact]
    public void GenerateRecoveryCode_ProducesValidFormattedCode()
    {
        string code = CrockfordBase32.GenerateRecoveryCode();

        Assert.NotNull(code);
        Assert.Equal(34, code.Length); // 28 chars + 6 hyphens
        string[] groups = code.Split('-');
        Assert.Equal(7, groups.Length);
        foreach (var group in groups)
        {
            Assert.Equal(4, group.Length);
        }

        bool valid = CrockfordBase32.TryDecode(code, out byte[] data);
        Assert.True(valid);
        Assert.Equal(16, data.Length);
    }

    [Fact]
    public void Roundtrip_EncodeAndDecode()
    {
        byte[] raw = new byte[16] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        string encoded = CrockfordBase32.Encode(raw);

        byte[] decoded = CrockfordBase32.Decode(encoded);
        Assert.Equal(raw, decoded);
    }

    [Fact]
    public void Checksum_DetectsCorruptedCharacter()
    {
        string code = CrockfordBase32.GenerateRecoveryCode();
        // Corrupt a character
        char[] chars = code.ToCharArray();
        chars[0] = chars[0] == 'A' ? 'B' : 'A';
        string corrupted = new string(chars);

        Assert.False(CrockfordBase32.TryDecode(corrupted, out _));
        Assert.Throws<FormatException>(() => CrockfordBase32.Decode(corrupted));
    }

    [Fact]
    public void Normalization_HandlesCaseAndCommonAliases()
    {
        byte[] raw = new byte[16] { 0xFF, 0x00, 0xAA, 0x55, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
        string encoded = CrockfordBase32.Encode(raw);

        // Lowercase and spaces
        string withSpaces = encoded.ToLowerInvariant().Replace('-', ' ');
        Assert.True(CrockfordBase32.TryDecode(withSpaces, out byte[] decoded));
        Assert.Equal(raw, decoded);
    }

    [Fact]
    public void GetGroups_ReturnsSevenGroups()
    {
        string code = CrockfordBase32.GenerateRecoveryCode();
        string[] groups = CrockfordBase32.GetGroups(code);
        Assert.Equal(7, groups.Length);
        Assert.Equal(code.Split('-'), groups);
    }
}
