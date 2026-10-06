namespace ApiKeyVault.Core.Storage;

public sealed record DeviceSecret(
    string LockboxId,
    byte[] DevicePrivateKey,
    byte[] IdentitySecret
);

public interface IDeviceKeyStore
{
    DeviceSecret? Get(string vaultId);
    void Set(string vaultId, DeviceSecret secret);
    void Remove(string vaultId);
}
