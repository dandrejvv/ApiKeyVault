using System.Security.Cryptography;
using System.Text;

namespace ApiKeyVault.Core.Cryptography;

public static class CrockfordBase32
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string GenerateRecoveryCode()
    {
        byte[] bytes = new byte[CryptoConstants.RecoveryCodeBytes];
        RandomNumberGenerator.Fill(bytes);
        return Encode(bytes);
    }

    public static string Encode(ReadOnlySpan<byte> data)
    {
        if (data.Length != 16)
        {
            throw new ArgumentException("Recovery code must be exactly 16 bytes (128 bits).", nameof(data));
        }

        // Encode 128 bits into 26 5-bit base32 symbols
        // 128 / 5 = 25 remainder 3. 26 symbols * 5 bits = 130 bits (2 padding bits at the end).
        Span<char> chars = stackalloc char[28];
        int charIndex = 0;
        int bitBuffer = 0;
        int bitCount = 0;

        foreach (byte b in data)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitCount += 8;

            while (bitCount >= 5)
            {
                bitCount -= 5;
                int index = (bitBuffer >> bitCount) & 0x1F;
                chars[charIndex++] = Alphabet[index];
            }
        }

        if (bitCount > 0)
        {
            int index = (bitBuffer << (5 - bitCount)) & 0x1F;
            chars[charIndex++] = Alphabet[index];
        }

        // charIndex is now 26 symbols.
        // Compute 2-character checksum (10 bits) over the 16 bytes
        (int c1, int c2) = ComputeChecksum(data);
        chars[charIndex++] = Alphabet[c1];
        chars[charIndex++] = Alphabet[c2];

        // Format as 7 groups of 4: XXXX-XXXX-XXXX-XXXX-XXXX-XXXX-XXXX
        var sb = new StringBuilder(34);
        for (int i = 0; i < 28; i++)
        {
            if (i > 0 && i % 4 == 0)
            {
                sb.Append('-');
            }
            sb.Append(chars[i]);
        }

        return sb.ToString();
    }

    public static bool TryDecode(string? input, out byte[] data)
    {
        data = [];
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        Span<char> normalized = stackalloc char[28];
        int normLen = 0;

        foreach (char c in input)
        {
            if (c == '-' || char.IsWhiteSpace(c))
            {
                continue;
            }

            if (normLen >= 28)
            {
                return false; // too long
            }

            char upper = char.ToUpperInvariant(c);
            // Crockford normalization:
            // I, i, L, l -> 1
            // O, o -> 0
            if (upper == 'I' || upper == 'L')
            {
                upper = '1';
            }
            else if (upper == 'O')
            {
                upper = '0';
            }

            if (!Alphabet.Contains(upper))
            {
                return false; // invalid character
            }

            normalized[normLen++] = upper;
        }

        if (normLen != 28)
        {
            return false;
        }

        // Decode first 26 symbols to 16 bytes
        byte[] result = new byte[16];
        int byteIndex = 0;
        int bitBuffer = 0;
        int bitCount = 0;

        for (int i = 0; i < 26; i++)
        {
            int val = Alphabet.IndexOf(normalized[i]);
            bitBuffer = (bitBuffer << 5) | val;
            bitCount += 5;

            if (bitCount >= 8)
            {
                bitCount -= 8;
                if (byteIndex < 16)
                {
                    result[byteIndex++] = (byte)((bitBuffer >> bitCount) & 0xFF);
                }
            }
        }

        // Verify checksum (symbols 26 and 27)
        (int expectedC1, int expectedC2) = ComputeChecksum(result);
        int actualC1 = Alphabet.IndexOf(normalized[26]);
        int actualC2 = Alphabet.IndexOf(normalized[27]);

        if (actualC1 != expectedC1 || actualC2 != expectedC2)
        {
            return false;
        }

        data = result;
        return true;
    }

    public static byte[] Decode(string input)
    {
        if (!TryDecode(input, out byte[] data))
        {
            throw new FormatException("Invalid recovery code or checksum mismatch.");
        }
        return data;
    }

    public static string[] GetGroups(string formattedCode)
    {
        // Extracts the 7 groups of 4
        if (!TryDecode(formattedCode, out byte[] raw))
        {
            throw new FormatException("Invalid recovery code.");
        }
        string standard = Encode(raw);
        return standard.Split('-');
    }

    private static (int c1, int c2) ComputeChecksum(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= (ushort)(b << 8);
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 0x8000) != 0)
                {
                    crc = (ushort)((crc << 1) ^ 0x1021);
                }
                else
                {
                    crc <<= 1;
                }
            }
        }
        int c1 = crc & 0x1F;
        int c2 = (crc >> 5) & 0x1F;
        return (c1, c2);
    }
}
