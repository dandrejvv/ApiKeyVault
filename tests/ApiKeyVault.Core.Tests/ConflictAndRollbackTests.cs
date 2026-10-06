using ApiKeyVault.Core.Cryptography;
using ApiKeyVault.Core.Model;
using ApiKeyVault.Core.Storage;
using ApiKeyVault.Core.Sync;
using ApiKeyVault.Core.Vault;

namespace ApiKeyVault.Core.Tests;

public class ConflictAndRollbackTests : IDisposable
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

    public ConflictAndRollbackTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "akv_conflict_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _vaultPath = Path.Combine(_tempDir, "vault.akv");
        _statePath = Path.Combine(_tempDir, "state.json");
        _deviceStore = new InMemoryDeviceKeyStore();
        _stateManager = new LocalStateManager(_statePath);
    }

    [Fact]
    public void RollbackDetection_LowerSaveCounter_ThrowsRollbackException()
    {
        var result = VaultManager.CreateVault(
            _vaultPath, "pass123", "PC1", FastKdf, _deviceStore, _stateManager);

        var session = result.Session;
        session.AddEntry("openai", "k1", "sec1");
        session.AddEntry("openai", "k2", "sec2");

        // Backup this version
        byte[] backupBytes = File.ReadAllBytes(_vaultPath);

        // Make another save so save counter advances
        session.AddEntry("openai", "k3", "sec3");

        // Restore backup file
        File.WriteAllBytes(_vaultPath, backupBytes);

        // Opening should detect rollback
        Assert.Throws<VaultRollbackException>(() =>
            VaultManager.OpenWithDevice(_vaultPath, _deviceStore, _stateManager, acceptRollback: false));

        // Opening with acceptRollback: true should succeed
        using var acceptedSession = VaultManager.OpenWithDevice(
            _vaultPath, _deviceStore, _stateManager, acceptRollback: true);
        Assert.NotNull(acceptedSession);
    }

    [Fact]
    public void ConflictCopyMerge_MergesEntriesAndArchivesConflictFile()
    {
        var result = VaultManager.CreateVault(
            _vaultPath, "pass123", "PC1", FastKdf, _deviceStore, _stateManager);

        var session = result.Session;
        session.AddEntry("openai", "key-a", "secret-a");

        // Simulate conflict copy created by OneDrive sync
        string conflictFile = Path.Combine(_tempDir, "vault-LAPTOP2.akv");
        File.Copy(_vaultPath, conflictFile);

        // PC1 makes an edit
        session.AddEntry("openai", "key-pc1", "secret-pc1");
        session.Dispose();

        // Simulate edit in conflict copy: unlock with passphrase, add entry, save
        using (var conflictSession = VaultManager.OpenWithPassphrase(
            conflictFile, "pass123", _deviceStore, _stateManager))
        {
            conflictSession.AddEntry("anthropic", "key-laptop2", "secret-laptop2");
        }

        // Open primary vault on PC1 with device: should detect and merge vault-LAPTOP2.akv
        using var mergedSession = VaultManager.OpenWithDevice(
            _vaultPath, _deviceStore, _stateManager);

        Assert.NotNull(mergedSession.FindEntry("openai/key-a"));
        Assert.NotNull(mergedSession.FindEntry("openai/key-pc1"));
        Assert.NotNull(mergedSession.FindEntry("anthropic/key-laptop2"));

        // Verify conflict file was archived to conflicts/
        string archivedConflict = Path.Combine(_tempDir, "conflicts", "vault-LAPTOP2.akv");
        Assert.True(File.Exists(archivedConflict));
        Assert.False(File.Exists(conflictFile));
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
