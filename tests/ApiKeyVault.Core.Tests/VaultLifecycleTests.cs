using ApiKeyVault.Core.Cryptography;
using ApiKeyVault.Core.Storage;
using ApiKeyVault.Core.Vault;

namespace ApiKeyVault.Core.Tests;

public class VaultLifecycleTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _vaultPath;
    private readonly string _statePath;
    private readonly InMemoryDeviceKeyStore _deviceStore;
    private readonly LocalStateManager _stateManager;

    private static readonly Argon2idParameters FastKdf = new()
    {
        MemoryKiB = CryptoConstants.MinMemoryKiB,
        Iterations = CryptoConstants.MinIterations,
        Parallelism = 1
    };

    public VaultLifecycleTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "akv_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _vaultPath = Path.Combine(_tempDir, "vault.akv");
        _statePath = Path.Combine(_tempDir, "state.json");
        _deviceStore = new InMemoryDeviceKeyStore();
        _stateManager = new LocalStateManager(_statePath);
    }

    [Fact]
    public void CreateVault_AndUnlockWithDevicePassphraseAndRecovery()
    {
        var result = VaultManager.CreateVault(
            _vaultPath,
            passphrase: "correct-horse-battery-staple",
            deviceName: "TEST-DEV-1",
            customKdfParams: FastKdf,
            deviceKeyStore: _deviceStore,
            localStateManager: _stateManager);

        Assert.NotNull(result.Session);
        Assert.NotNull(result.RecoveryCode);

        // 1. Unlock with device
        using var devSession = VaultManager.OpenWithDevice(
            _vaultPath, _deviceStore, _stateManager);
        Assert.Equal("TEST-DEV-1", devSession.CurrentDeviceName);
        Assert.True(devSession.IsDeviceEnrolled);

        // 2. Unlock with passphrase
        using var passSession = VaultManager.OpenWithPassphrase(
            _vaultPath, "correct-horse-battery-staple", _deviceStore, _stateManager);
        Assert.NotNull(passSession);

        // 3. Unlock with recovery code
        using var recSession = VaultManager.OpenWithRecovery(
            _vaultPath, result.RecoveryCode, _deviceStore, _stateManager);
        Assert.NotNull(recSession);
    }

    [Fact]
    public void EnrolAndRemoveDevice_RotatesVaultKeyAndRevokesOldDevice()
    {
        var result = VaultManager.CreateVault(
            _vaultPath, "master-pass", "MAIN-PC", FastKdf, _deviceStore, _stateManager);

        using var session = result.Session;

        // Enrol secondary device
        var dev2Key = CryptoPrimitives.GenerateRandomBytes(32);
        session.EnrolDevice("LAPTOP-02", dev2Key);

        Assert.Contains(session.Payload.LockboxRegistry, r => r.Name == "LAPTOP-02");

        // Remove LAPTOP-02
        session.RemoveDevices(["LAPTOP-02"]);

        Assert.DoesNotContain(session.Header.Lockboxes, l => l.Kind == "device" && session.Payload.LockboxRegistry.Any(r => r.LockboxId == l.LockboxId && r.Name == "LAPTOP-02"));
        Assert.Contains(session.Payload.Revoked, r => r.LockboxId != null);

        // Trying to remove this device itself must fail
        Assert.Throws<InvalidOperationException>(() => session.RemoveDevices(["MAIN-PC"]));
    }

    [Fact]
    public void ChangePassphrase_RevokesOldPassphrase()
    {
        var result = VaultManager.CreateVault(
            _vaultPath, "old-pass", "MAIN-PC", FastKdf, _deviceStore, _stateManager);

        using var session = result.Session;
        session.ChangePassphrase("new-super-pass", FastKdf);

        // Old passphrase fails
        Assert.Throws<VaultAccessDeniedException>(() =>
            VaultManager.OpenWithPassphrase(_vaultPath, "old-pass", _deviceStore, _stateManager));

        // New passphrase succeeds
        using var newSession = VaultManager.OpenWithPassphrase(
            _vaultPath, "new-super-pass", _deviceStore, _stateManager);
        Assert.NotNull(newSession);
    }

    [Fact]
    public void RegenerateRecoveryCode_RevokesOldCode()
    {
        var result = VaultManager.CreateVault(
            _vaultPath, "test-pass", "MAIN-PC", FastKdf, _deviceStore, _stateManager);

        string oldCode = result.RecoveryCode;
        string newCode = result.Session.RegenerateRecoveryCode();

        Assert.NotEqual(oldCode, newCode);

        // Old recovery code fails
        Assert.Throws<VaultAccessDeniedException>(() =>
            VaultManager.OpenWithRecovery(_vaultPath, oldCode, _deviceStore, _stateManager));

        // New recovery code succeeds
        using var newSession = VaultManager.OpenWithRecovery(
            _vaultPath, newCode, _deviceStore, _stateManager);
        Assert.NotNull(newSession);
    }

    [Fact]
    public void EntryOperations_AddEditRotateUndoDelete()
    {
        var result = VaultManager.CreateVault(
            _vaultPath, "test-pass", "MAIN-PC", FastKdf, _deviceStore, _stateManager);
        var session = result.Session;

        // Add
        var entry = session.AddEntry("openai", "work-key", "sk-test-secret-1", comment: "My work key");
        Assert.NotNull(entry);
        Assert.Equal("openai", entry.Provider);
        Assert.Equal("work-key", entry.Name);
        Assert.Equal("sk-test-secret-1", entry.Secret);

        // Duplicate throws
        Assert.Throws<InvalidOperationException>(() =>
            session.AddEntry("openai", "work-key", "sk-other"));

        // Edit
        session.EditEntry("openai/work-key", newComment: "Updated comment");
        Assert.Equal("Updated comment", session.FindEntry("openai/work-key")!.Comment);

        // Rotate
        session.RotateSecret("openai/work-key", "sk-test-secret-2");
        var rotated = session.FindEntry("openai/work-key")!;
        Assert.Equal("sk-test-secret-2", rotated.Secret);
        Assert.Equal("sk-test-secret-1", rotated.PreviousSecret);

        // Undo rotation
        session.UndoRotation("openai/work-key");
        var restored = session.FindEntry("openai/work-key")!;
        Assert.Equal("sk-test-secret-1", restored.Secret);
        Assert.Null(restored.PreviousSecret);

        // Delete
        session.DeleteEntry("openai/work-key");
        Assert.Null(session.FindEntry("openai/work-key"));
        Assert.Contains(session.Payload.Tombstones, t => t.Id == entry.Id);
    }

    [Fact]
    public void Tampering_FlippingByteInFile_ThrowsVaultTamperedException()
    {
        VaultManager.CreateVault(
            _vaultPath, "test-pass", "MAIN-PC", FastKdf, _deviceStore, _stateManager);

        byte[] bytes = File.ReadAllBytes(_vaultPath);
        // Flip byte in payload ciphertext
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(_vaultPath, bytes);

        Assert.Throws<VaultTamperedException>(() =>
            VaultManager.OpenWithDevice(_vaultPath, _deviceStore, _stateManager));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }
}
