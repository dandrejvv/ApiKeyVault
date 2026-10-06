namespace ApiKeyVault.Core;

public class VaultException : Exception
{
    public VaultException(string message) : base(message) { }
    public VaultException(string message, Exception innerException) : base(message, innerException) { }
}

public class VaultNotFoundException : VaultException
{
    public VaultNotFoundException(string path) : base($"Vault file was not found at '{path}'.") { }
}

public class UnsupportedVaultVersionException : VaultException
{
    public uint FileVersion { get; }
    public UnsupportedVaultVersionException(uint fileVersion)
        : base($"This vault was saved by a newer version of ApiKeyVault (version {fileVersion}).")
    {
        FileVersion = fileVersion;
    }
}

public class VaultTamperedException : VaultException
{
    public VaultTamperedException(string message) : base(message) { }
    public VaultTamperedException(string message, Exception innerException) : base(message, innerException) { }
}

public class VaultRollbackException : VaultException
{
    public ulong FileCounter { get; }
    public ulong KnownCounter { get; }
    public bool HasRevokedPadlock { get; }

    public VaultRollbackException(ulong fileCounter, ulong knownCounter, bool hasRevokedPadlock)
        : base($"This vault is older than one this device has already seen (save {fileCounter} vs {knownCounter}). Changes may be missing.")
    {
        FileCounter = fileCounter;
        KnownCounter = knownCounter;
        HasRevokedPadlock = hasRevokedPadlock;
    }
}

public class VaultAccessDeniedException : VaultException
{
    public VaultAccessDeniedException(string message) : base(message) { }
    public VaultAccessDeniedException(string message, Exception innerException) : base(message, innerException) { }
}
