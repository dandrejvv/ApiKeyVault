namespace ApiKeyVault.Core.Cryptography;

public static class CryptoConstants
{
    public const string Magic = "AKV1";
    public const uint CurrentFormatVersion = 1;

    public const int VaultKeySize = 32;       // 256 bits
    public const int NonceSize = 24;          // 192 bits for XChaCha20-Poly1305
    public const int X25519KeySize = 32;      // 256 bits
    public const int IdentitySecretSize = 32; // 256 bits
    public const int RecoveryCodeBytes = 16;  // 128 bits
    public const int SaltSize = 16;           // 128 bits for Argon2id

    public const string LockboxHkdfInfo = "akv/lockbox/v1";
    public const string RecoveryHkdfInfo = "akv/recovery/v1";
    public const string IdentityTagHmacPrefix = "akv/id/v1";

    // Argon2id bounds
    public const int MinMemoryKiB = 19 * 1024;    // 19 MiB
    public const int MaxMemoryKiB = 256 * 1024;   // 256 MiB
    public const int DefaultMemoryKiB = 64 * 1024;// 64 MiB
    public const int MinIterations = 2;
    public const int MaxIterations = 64;
    public const int DefaultIterations = 3;
    public const int DegreeOfParallelism = 1;
}
