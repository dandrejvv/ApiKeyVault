using System.Security.Cryptography;
using System.Text;

namespace ApiKeyVault.Core.Cryptography;

public sealed class UnsealedSecret : IDisposable
{
    private byte[] _secretBytes;
    private bool _disposed;

    internal UnsealedSecret(byte[] secretBytes)
    {
        _secretBytes = secretBytes;
    }

    public ReadOnlySpan<byte> Bytes
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _secretBytes;
        }
    }

    public string ToUtf8String()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Encoding.UTF8.GetString(_secretBytes);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            CryptographicOperations.ZeroMemory(_secretBytes);
            _secretBytes = [];
            _disposed = true;
        }
    }
}

public sealed class SealedSecret
{
    private readonly byte[] _ciphertext;
    private readonly byte[] _nonce;
    private readonly byte[] _tag;
    private readonly byte[] _sessionKey;

    public SealedSecret(ReadOnlySpan<byte> plaintextSecretBytes)
    {
        _sessionKey = new byte[32];
        RandomNumberGenerator.Fill(_sessionKey);

        _nonce = new byte[12];
        RandomNumberGenerator.Fill(_nonce);

        _ciphertext = new byte[plaintextSecretBytes.Length];
        _tag = new byte[16];

        using var aesGcm = new AesGcm(_sessionKey, 16);
        aesGcm.Encrypt(_nonce, plaintextSecretBytes, _ciphertext, _tag);
    }

    public SealedSecret(string plaintextSecret)
        : this(Encoding.UTF8.GetBytes(plaintextSecret))
    {
    }

    public UnsealedSecret Unseal()
    {
        byte[] plaintext = new byte[_ciphertext.Length];
        try
        {
            using var aesGcm = new AesGcm(_sessionKey, 16);
            aesGcm.Decrypt(_nonce, _ciphertext, _tag, plaintext);
            return new UnsealedSecret(plaintext);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
    }

    public string UnsealToString()
    {
        using var unsealed = Unseal();
        return unsealed.ToUtf8String();
    }
}
