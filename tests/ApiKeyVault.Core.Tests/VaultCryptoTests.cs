using System.Security.Cryptography;
using System.Text;
using ApiKeyVault.Core.Cryptography;

namespace ApiKeyVault.Core.Tests;

public class VaultCryptoTests
{
    private static readonly Argon2idParameters FastKdf = new()
    {
        MemoryKiB = CryptoConstants.MinMemoryKiB,
        Iterations = CryptoConstants.MinIterations,
        Parallelism = 1
    };

    [Fact]
    public void PassphraseDerivation_ProducesValidKeys()
    {
        byte[] salt = CryptoPrimitives.GenerateRandomBytes(CryptoConstants.SaltSize);
        var result = CryptoPrimitives.DerivePassphraseOwner("test-passphrase", salt, FastKdf);

        Assert.Equal(CryptoConstants.X25519KeySize, result.RecipientPrivateKey.Length);
        Assert.Equal(CryptoConstants.X25519KeySize, result.RecipientPublicKey.Length);
        Assert.NotNull(result.TagKey);
        Assert.Equal(32, result.TagKey.Length);
    }

    [Fact]
    public void RecoveryDerivation_ProducesValidKeys()
    {
        byte[] recoveryCode = CryptoPrimitives.GenerateRandomBytes(16);
        var result = CryptoPrimitives.DeriveRecoveryOwner(recoveryCode);

        Assert.Equal(CryptoConstants.X25519KeySize, result.RecipientPrivateKey.Length);
        Assert.Equal(CryptoConstants.X25519KeySize, result.RecipientPublicKey.Length);
        Assert.NotNull(result.TagKey);
        Assert.Equal(32, result.TagKey.Length);
    }

    [Fact]
    public void WrapAndUnwrapVaultKey_RoundtripsSuccessfully()
    {
        byte[] vaultKey = CryptoPrimitives.GenerateRandomBytes(CryptoConstants.VaultKeySize);
        var owner = CryptoPrimitives.GenerateDeviceOwner();
        string vaultId = Guid.NewGuid().ToString("N");
        string lockboxId = Guid.NewGuid().ToString("N");

        var (ephPub, nonce, wrapped) = CryptoPrimitives.WrapVaultKey(
            vaultKey, owner.RecipientPublicKey, vaultId, lockboxId);

        byte[] unwrapped = CryptoPrimitives.UnwrapVaultKey(
            wrapped, nonce, ephPub, owner.RecipientPublicKey, owner.RecipientPrivateKey, vaultId, lockboxId);

        Assert.Equal(vaultKey, unwrapped);
    }

    [Fact]
    public void TamperedLockbox_ThrowsCryptographicException()
    {
        byte[] vaultKey = CryptoPrimitives.GenerateRandomBytes(CryptoConstants.VaultKeySize);
        var owner = CryptoPrimitives.GenerateDeviceOwner();
        string vaultId = Guid.NewGuid().ToString("N");
        string lockboxId = Guid.NewGuid().ToString("N");

        var (ephPub, nonce, wrapped) = CryptoPrimitives.WrapVaultKey(
            vaultKey, owner.RecipientPublicKey, vaultId, lockboxId);

        // Flip a byte in wrapped key
        wrapped[0] ^= 0xFF;

        Assert.Throws<CryptographicException>(() =>
            CryptoPrimitives.UnwrapVaultKey(wrapped, nonce, ephPub, owner.RecipientPublicKey, owner.RecipientPrivateKey, vaultId, lockboxId));
    }

    [Fact]
    public void IdentityTag_MatchesExpectedValue()
    {
        byte[] tagKey = CryptoPrimitives.GenerateRandomBytes(32);
        string vaultId = Guid.NewGuid().ToString("N");
        byte[] idSecret = CryptoPrimitives.GenerateRandomBytes(32);

        byte[] tag = CryptoPrimitives.ComputeIdentityTag(tagKey, vaultId, idSecret);

        Assert.True(CryptoPrimitives.VerifyIdentityTag(tagKey, vaultId, idSecret, tag));

        // Wrong tag key
        byte[] wrongKey = CryptoPrimitives.GenerateRandomBytes(32);
        Assert.False(CryptoPrimitives.VerifyIdentityTag(wrongKey, vaultId, idSecret, tag));

        // Wrong id secret
        byte[] wrongSecret = CryptoPrimitives.GenerateRandomBytes(32);
        Assert.False(CryptoPrimitives.VerifyIdentityTag(tagKey, vaultId, wrongSecret, tag));
    }

    [Fact]
    public void SealAndUnsealPayload_RoundtripsSuccessfully()
    {
        byte[] vaultKey = CryptoPrimitives.GenerateRandomBytes(CryptoConstants.VaultKeySize);
        byte[] aad = Encoding.UTF8.GetBytes("header-aad-data");
        byte[] payload = Encoding.UTF8.GetBytes("payload-contents-to-encrypt");

        (byte[] nonce, byte[] ciphertext) = CryptoPrimitives.SealPayload(vaultKey, aad, payload);

        byte[] unsealed = CryptoPrimitives.UnsealPayload(vaultKey, aad, nonce, ciphertext);
        Assert.Equal(payload, unsealed);
    }

    [Fact]
    public void TamperedAad_FailsUnseal()
    {
        byte[] vaultKey = CryptoPrimitives.GenerateRandomBytes(CryptoConstants.VaultKeySize);
        byte[] aad = Encoding.UTF8.GetBytes("header-aad-data");
        byte[] payload = Encoding.UTF8.GetBytes("payload-contents-to-encrypt");

        (byte[] nonce, byte[] ciphertext) = CryptoPrimitives.SealPayload(vaultKey, aad, payload);

        byte[] tamperedAad = Encoding.UTF8.GetBytes("tampered-header-aad");
        Assert.Throws<CryptographicException>(() =>
            CryptoPrimitives.UnsealPayload(vaultKey, tamperedAad, nonce, ciphertext));
    }

    [Fact]
    public void SealedSecret_DecryptsAndCleansMemory()
    {
        string secretText = "super-secret-api-token-123";
        var sealedSecret = new SealedSecret(secretText);

        using (var unsealed = sealedSecret.Unseal())
        {
            Assert.Equal(secretText, unsealed.ToUtf8String());
        }

        Assert.Equal(secretText, sealedSecret.UnsealToString());
    }
}
