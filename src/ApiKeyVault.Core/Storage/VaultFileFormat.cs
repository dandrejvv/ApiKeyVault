using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApiKeyVault.Core.Cryptography;
using ApiKeyVault.Core.Model;

namespace ApiKeyVault.Core.Storage;

public sealed record ParsedVaultFile(
    VaultHeader Header,
    byte[] Aad,
    byte[] Nonce,
    byte[] Ciphertext
);

public static class VaultFileFormat
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    public static byte[] Serialize(VaultHeader header, VaultPayload payload, ReadOnlySpan<byte> vaultKey)
    {
        byte[] headerBytes = JsonSerializer.SerializeToUtf8Bytes(header, JsonOptions);
        byte[] payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);

        try
        {
            // Magic (4 bytes) + Version (4 bytes) + HeaderLength (4 bytes) + HeaderBytes
            int aadLength = 4 + 4 + 4 + headerBytes.Length;
            byte[] aad = new byte[aadLength];

            Encoding.ASCII.GetBytes(CryptoConstants.Magic, aad.AsSpan(0, 4));
            BinaryPrimitives.WriteUInt32LittleEndian(aad.AsSpan(4, 4), CryptoConstants.CurrentFormatVersion);
            BinaryPrimitives.WriteUInt32LittleEndian(aad.AsSpan(8, 4), (uint)headerBytes.Length);
            headerBytes.CopyTo(aad.AsSpan(12));

            // Encrypt and seal payload
            (byte[] nonce, byte[] ciphertext) = CryptoPrimitives.SealPayload(vaultKey, aad, payloadBytes);

            byte[] fullFile = new byte[aadLength + nonce.Length + ciphertext.Length];
            Buffer.BlockCopy(aad, 0, fullFile, 0, aadLength);
            Buffer.BlockCopy(nonce, 0, fullFile, aadLength, nonce.Length);
            Buffer.BlockCopy(ciphertext, 0, fullFile, aadLength + nonce.Length, ciphertext.Length);

            return fullFile;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payloadBytes);
        }
    }

    public static ParsedVaultFile ParseContainer(ReadOnlySpan<byte> fileBytes)
    {
        if (fileBytes.Length < 12 + CryptoConstants.NonceSize + 16) // min length: 12 aad header prefix + empty header + nonce + 16-byte poly1305 tag
        {
            throw new VaultTamperedException("The vault file is truncated or corrupted.");
        }

        // Magic check
        string magic = Encoding.ASCII.GetString(fileBytes[..4]);
        if (magic != CryptoConstants.Magic)
        {
            throw new VaultTamperedException("Invalid vault file magic. Expected 'AKV1'.");
        }

        // Version check
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(fileBytes.Slice(4, 4));
        if (version > CryptoConstants.CurrentFormatVersion)
        {
            throw new UnsupportedVaultVersionException(version);
        }

        uint headerLength = BinaryPrimitives.ReadUInt32LittleEndian(fileBytes.Slice(8, 4));
        int aadLength = 12 + (int)headerLength;

        if (fileBytes.Length < aadLength + CryptoConstants.NonceSize + 16)
        {
            throw new VaultTamperedException("The vault file is corrupted or header length is invalid.");
        }

        byte[] aad = fileBytes[..aadLength].ToArray();
        ReadOnlySpan<byte> headerBytes = fileBytes.Slice(12, (int)headerLength);

        VaultHeader header;
        try
        {
            header = JsonSerializer.Deserialize<VaultHeader>(headerBytes, JsonOptions)
                ?? throw new VaultTamperedException("Failed to parse vault header JSON.");
        }
        catch (Exception ex) when (ex is not VaultException)
        {
            throw new VaultTamperedException("Corrupted vault header.", ex);
        }

        byte[] nonce = fileBytes.Slice(aadLength, CryptoConstants.NonceSize).ToArray();
        byte[] ciphertext = fileBytes[(aadLength + CryptoConstants.NonceSize)..].ToArray();

        return new ParsedVaultFile(header, aad, nonce, ciphertext);
    }

    public static VaultPayload UnsealPayload(ParsedVaultFile parsed, ReadOnlySpan<byte> vaultKey)
    {
        byte[] decryptedBytes;
        try
        {
            decryptedBytes = CryptoPrimitives.UnsealPayload(vaultKey, parsed.Aad, parsed.Nonce, parsed.Ciphertext);
        }
        catch (CryptographicException ex)
        {
            throw new VaultTamperedException("Vault seal verification failed. File has been tampered with or corrupted.", ex);
        }

        try
        {
            var payload = JsonSerializer.Deserialize<VaultPayload>(decryptedBytes, JsonOptions);
            return payload ?? throw new VaultTamperedException("Corrupted vault payload JSON.");
        }
        catch (Exception ex) when (ex is not VaultException)
        {
            throw new VaultTamperedException("Failed to deserialize vault payload.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decryptedBytes);
        }
    }
}
