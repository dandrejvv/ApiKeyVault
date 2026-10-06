using System.Security.Cryptography;
using System.Text;
using NSec.Cryptography;

namespace ApiKeyVault.Core.Cryptography;

public enum LockboxKind
{
    Passphrase,
    Recovery,
    Device
}

public sealed record OwnerKeyDerivationResult(
    byte[] RecipientPrivateKey,
    byte[] RecipientPublicKey,
    byte[]? TagKey
);

public static class CryptoPrimitives
{
    private static readonly KeyAgreementAlgorithm X25519 = KeyAgreementAlgorithm.X25519;
    private static readonly AeadAlgorithm XChaCha20Poly1305 = AeadAlgorithm.XChaCha20Poly1305;
    private static readonly KeyDerivationAlgorithm HkdfSha256 = KeyDerivationAlgorithm.HkdfSha256;
    private static readonly MacAlgorithm HmacSha256 = MacAlgorithm.HmacSha256;

    // Note: KeyCreationParameters is a ref struct in NSec, so create on stack where needed.

    public static byte[] GenerateRandomBytes(int count)
    {
        byte[] bytes = new byte[count];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return bytes;
    }

    public static OwnerKeyDerivationResult DerivePassphraseOwner(string passphrase, byte[] salt, Argon2idParameters parameters)
    {
        parameters.Validate();
        if (salt.Length != CryptoConstants.SaltSize)
        {
            throw new ArgumentException($"Salt must be {CryptoConstants.SaltSize} bytes.", nameof(salt));
        }

        var nsecParams = new NSec.Cryptography.Argon2Parameters
        {
            MemorySize = parameters.MemoryKiB,
            NumberOfPasses = parameters.Iterations,
            DegreeOfParallelism = parameters.Parallelism
        };

        var argon2 = PasswordBasedKeyDerivationAlgorithm.Argon2id(in nsecParams);
        byte[] derived = argon2.DeriveBytes(passphrase, salt, 64);

        try
        {
            byte[] privKey = new byte[CryptoConstants.X25519KeySize];
            byte[] tagKey = new byte[32];
            Buffer.BlockCopy(derived, 0, privKey, 0, 32);
            Buffer.BlockCopy(derived, 32, tagKey, 0, 32);

            byte[] pubKey = ComputeX25519PublicKey(privKey);

            return new OwnerKeyDerivationResult(privKey, pubKey, tagKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derived);
        }
    }

    public static OwnerKeyDerivationResult DeriveRecoveryOwner(ReadOnlySpan<byte> recoveryCodeBytes)
    {
        if (recoveryCodeBytes.Length != CryptoConstants.RecoveryCodeBytes)
        {
            throw new ArgumentException($"Recovery code bytes must be {CryptoConstants.RecoveryCodeBytes} bytes.", nameof(recoveryCodeBytes));
        }

        // Expand 16-byte recovery code to 64 bytes using HKDF-SHA256
        byte[] info = Encoding.UTF8.GetBytes(CryptoConstants.RecoveryHkdfInfo);
        byte[] derived = System.Security.Cryptography.HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            recoveryCodeBytes.ToArray(),
            64,
            salt: null,
            info: info);

        try
        {
            byte[] privKey = new byte[CryptoConstants.X25519KeySize];
            byte[] tagKey = new byte[32];
            Buffer.BlockCopy(derived, 0, privKey, 0, 32);
            Buffer.BlockCopy(derived, 32, tagKey, 0, 32);

            byte[] pubKey = ComputeX25519PublicKey(privKey);

            return new OwnerKeyDerivationResult(privKey, pubKey, tagKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derived);
        }
    }

    public static OwnerKeyDerivationResult GenerateDeviceOwner()
    {
        byte[] privKey = GenerateRandomBytes(CryptoConstants.X25519KeySize);
        byte[] pubKey = ComputeX25519PublicKey(privKey);
        return new OwnerKeyDerivationResult(privKey, pubKey, null);
    }

    public static byte[] ComputeX25519PublicKey(ReadOnlySpan<byte> privateKey)
    {
        var creationParams = new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
        using var key = Key.Import(X25519, privateKey, KeyBlobFormat.RawPrivateKey, in creationParams);
        return key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
    }

    public static (byte[] EphPub, byte[] Nonce, byte[] WrappedVaultKey) WrapVaultKey(
        ReadOnlySpan<byte> vaultKey,
        ReadOnlySpan<byte> recipientPub,
        string vaultId,
        string lockboxId)
    {
        var creationParams = new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
        using var ephKey = Key.Create(X25519, in creationParams);
        byte[] ephPub = ephKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);

        var recipientPublicKey = PublicKey.Import(X25519, recipientPub, KeyBlobFormat.RawPublicKey);
        using var sharedSecret = X25519.Agree(ephKey, recipientPublicKey)
            ?? throw new InvalidOperationException("X25519 key agreement failed.");

        byte[] hkdfSalt = new byte[ephPub.Length + recipientPub.Length];
        Buffer.BlockCopy(ephPub, 0, hkdfSalt, 0, ephPub.Length);
        recipientPub.CopyTo(hkdfSalt.AsSpan(ephPub.Length));

        byte[] info = Encoding.UTF8.GetBytes(CryptoConstants.LockboxHkdfInfo);
        byte[] wrapKeyBytes = HkdfSha256.DeriveBytes(sharedSecret, hkdfSalt, info, 32);

        byte[] nonce = GenerateRandomBytes(CryptoConstants.NonceSize);
        byte[] aad = Encoding.UTF8.GetBytes(vaultId + lockboxId);

        try
        {
            using var wrapKey = Key.Import(XChaCha20Poly1305, wrapKeyBytes, KeyBlobFormat.RawSymmetricKey);
            byte[] wrapped = XChaCha20Poly1305.Encrypt(wrapKey, nonce, aad, vaultKey);
            return (ephPub, nonce, wrapped);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrapKeyBytes);
        }
    }

    public static byte[] UnwrapVaultKey(
        ReadOnlySpan<byte> wrappedVaultKey,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> ephPub,
        ReadOnlySpan<byte> recipientPub,
        ReadOnlySpan<byte> recipientPriv,
        string vaultId,
        string lockboxId)
    {
        var creationParams = new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport };
        using var privKey = Key.Import(X25519, recipientPriv, KeyBlobFormat.RawPrivateKey, in creationParams);
        var ephPublicKey = PublicKey.Import(X25519, ephPub, KeyBlobFormat.RawPublicKey);

        using var sharedSecret = X25519.Agree(privKey, ephPublicKey)
            ?? throw new CryptographicException("X25519 key agreement failed.");

        byte[] hkdfSalt = new byte[ephPub.Length + recipientPub.Length];
        ephPub.CopyTo(hkdfSalt.AsSpan(0));
        recipientPub.CopyTo(hkdfSalt.AsSpan(ephPub.Length));

        byte[] info = Encoding.UTF8.GetBytes(CryptoConstants.LockboxHkdfInfo);
        byte[] wrapKeyBytes = HkdfSha256.DeriveBytes(sharedSecret, hkdfSalt, info, 32);

        byte[] aad = Encoding.UTF8.GetBytes(vaultId + lockboxId);

        try
        {
            using var wrapKey = Key.Import(XChaCha20Poly1305, wrapKeyBytes, KeyBlobFormat.RawSymmetricKey);
            byte[]? decrypted = XChaCha20Poly1305.Decrypt(wrapKey, nonce, aad, wrappedVaultKey);
            return decrypted ?? throw new CryptographicException("Lockbox authentication failed (tampered lockbox or wrong key).");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrapKeyBytes);
        }
    }

    public static byte[] ComputeIdentityTag(ReadOnlySpan<byte> tagKey, string vaultId, ReadOnlySpan<byte> identitySecret)
    {
        // id_tag = HMAC-SHA256(tag_key, "akv/id/v1" || vault_id || identity_secret)
        byte[] prefix = Encoding.UTF8.GetBytes(CryptoConstants.IdentityTagHmacPrefix);
        byte[] vaultIdBytes = Encoding.UTF8.GetBytes(vaultId);

        byte[] message = new byte[prefix.Length + vaultIdBytes.Length + identitySecret.Length];
        Buffer.BlockCopy(prefix, 0, message, 0, prefix.Length);
        Buffer.BlockCopy(vaultIdBytes, 0, message, prefix.Length, vaultIdBytes.Length);
        identitySecret.CopyTo(message.AsSpan(prefix.Length + vaultIdBytes.Length));

        using var hmacKey = Key.Import(HmacSha256, tagKey, KeyBlobFormat.RawSymmetricKey);
        return HmacSha256.Mac(hmacKey, message);
    }

    public static bool VerifyIdentityTag(ReadOnlySpan<byte> tagKey, string vaultId, ReadOnlySpan<byte> identitySecret, ReadOnlySpan<byte> expectedTag)
    {
        byte[] computed = ComputeIdentityTag(tagKey, vaultId, identitySecret);
        return CryptographicOperations.FixedTimeEquals(computed, expectedTag);
    }

    public static (byte[] Nonce, byte[] Ciphertext) SealPayload(ReadOnlySpan<byte> vaultKey, ReadOnlySpan<byte> aad, ReadOnlySpan<byte> payloadBytes)
    {
        byte[] nonce = GenerateRandomBytes(CryptoConstants.NonceSize);
        using var key = Key.Import(XChaCha20Poly1305, vaultKey, KeyBlobFormat.RawSymmetricKey);
        byte[] ciphertext = XChaCha20Poly1305.Encrypt(key, nonce, aad, payloadBytes);
        return (nonce, ciphertext);
    }

    public static byte[] UnsealPayload(ReadOnlySpan<byte> vaultKey, ReadOnlySpan<byte> aad, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext)
    {
        using var key = Key.Import(XChaCha20Poly1305, vaultKey, KeyBlobFormat.RawSymmetricKey);
        byte[]? decrypted = XChaCha20Poly1305.Decrypt(key, nonce, aad, ciphertext);
        return decrypted ?? throw new CryptographicException("Vault seal is invalid. Header or payload has been tampered with.");
    }
}
