using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Meziantou.Framework.Win32;

namespace ApiKeyVault.Core.Storage;

public static class DeviceKeyStoreFactory
{
    public static IDeviceKeyStore CreateDefault()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsCredentialManagerKeyStore();
        }
        return new InMemoryDeviceKeyStore();
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialManagerKeyStore : IDeviceKeyStore
{
    private static string GetTarget(string vaultId) => $"ApiKeyVault/{vaultId}";

    public DeviceSecret? Get(string vaultId)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }

        string target = GetTarget(vaultId);
        var cred = CredentialManager.ReadCredential(target);
        if (cred == null || string.IsNullOrEmpty(cred.Password))
        {
            return null;
        }

        try
        {
            byte[] combined = Convert.FromBase64String(cred.Password);
            if (combined.Length != 64)
            {
                return null;
            }

            byte[] privKey = new byte[32];
            byte[] idSecret = new byte[32];
            Buffer.BlockCopy(combined, 0, privKey, 0, 32);
            Buffer.BlockCopy(combined, 32, idSecret, 0, 32);

            string lockboxId = cred.UserName ?? string.Empty;
            return new DeviceSecret(lockboxId, privKey, idSecret);
        }
        catch
        {
            return null;
        }
    }

    public void Set(string vaultId, DeviceSecret secret)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        string target = GetTarget(vaultId);
        byte[] combined = new byte[64];
        Buffer.BlockCopy(secret.DevicePrivateKey, 0, combined, 0, 32);
        Buffer.BlockCopy(secret.IdentitySecret, 0, combined, 32, 32);
        string secretBase64 = Convert.ToBase64String(combined);

        CredentialManager.WriteCredential(
            applicationName: target,
            userName: secret.LockboxId,
            secret: secretBase64,
            comment: "ApiKeyVault Device Credential",
            persistence: CredentialPersistence.LocalMachine);
    }

    public void Remove(string vaultId)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        string target = GetTarget(vaultId);
        CredentialManager.DeleteCredential(target);
    }
}

public sealed class InMemoryDeviceKeyStore : IDeviceKeyStore
{
    private readonly Dictionary<string, DeviceSecret> _store = new(StringComparer.OrdinalIgnoreCase);

    public DeviceSecret? Get(string vaultId)
    {
        if (_store.TryGetValue(vaultId, out var secret))
        {
            // return defensive copy
            return new DeviceSecret(
                secret.LockboxId,
                (byte[])secret.DevicePrivateKey.Clone(),
                (byte[])secret.IdentitySecret.Clone());
        }
        return null;
    }

    public void Set(string vaultId, DeviceSecret secret)
    {
        _store[vaultId] = new DeviceSecret(
            secret.LockboxId,
            (byte[])secret.DevicePrivateKey.Clone(),
            (byte[])secret.IdentitySecret.Clone());
    }

    public void Remove(string vaultId)
    {
        _store.Remove(vaultId);
    }
}
