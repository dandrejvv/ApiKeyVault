using ApiKeyVault.Core.Cryptography;
using ApiKeyVault.Core.Storage;
using ApiKeyVault.Core.Vault;
using ApiKeyVault.UI.ViewModels;

namespace ApiKeyVault.UI.Tests;

public class MainWindowViewModelTests : IDisposable
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

    public MainWindowViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "akv_ui_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _vaultPath = Path.Combine(_tempDir, "vault.akv");
        _statePath = Path.Combine(_tempDir, "state.json");
        _deviceStore = new InMemoryDeviceKeyStore();
        _stateManager = new LocalStateManager(_statePath);
    }

    private MainWindowViewModel CreateViewModel() => new(_deviceStore, _stateManager, defaultVaultPath: _vaultPath);

    [Fact]
    public void InitialState_WhenVaultDoesNotExist_ShowsFirstRun()
    {
        var vm = CreateViewModel();
        Assert.True(vm.IsFirstRun);
        Assert.False(vm.IsUnlocked);
        Assert.False(vm.IsLocked);
    }

    [Fact]
    public void CreateVault_TransitionsToUnlocked()
    {
        var vm = CreateViewModel();
        vm.FirstRunVaultPath = _vaultPath;
        vm.FirstRunPassphrase = "test-passphrase-123";
        vm.FirstRunConfirmPassphrase = "test-passphrase-123";

        vm.CreateVaultCommand.Execute(null);

        Assert.True(vm.IsUnlocked);
        Assert.False(vm.IsFirstRun);
        Assert.False(vm.IsLocked);
        Assert.NotNull(vm.FirstRunRecoveryCode);
        Assert.True(File.Exists(_vaultPath));
    }

    [Fact]
    public void LockAndUnlock_Flow()
    {
        VaultManager.CreateVault(_vaultPath, "test-pass", "MAIN-PC", FastKdf, _deviceStore, _stateManager);

        var vm = CreateViewModel();
        // By default should unlock via device store
        Assert.True(vm.IsUnlocked);

        // Lock
        vm.LockCommand.Execute(null);
        Assert.False(vm.IsUnlocked);
        Assert.True(vm.IsLocked);

        // Unlock with wrong passphrase
        vm.UnlockPassphrase = "wrong-pass";
        vm.UnlockCommand.Execute(null);
        Assert.False(vm.IsUnlocked);
        Assert.NotNull(vm.UnlockErrorMessage);

        // Unlock with correct passphrase
        vm.UnlockPassphrase = "test-pass";
        vm.UnlockCommand.Execute(null);
        Assert.True(vm.IsUnlocked);
        Assert.False(vm.IsLocked);
    }

    [Fact]
    public void Entries_SearchAndFiltering()
    {
        var createResult = VaultManager.CreateVault(_vaultPath, "test-pass", "MAIN-PC", FastKdf, _deviceStore, _stateManager);
        var session = createResult.Session;

        session.AddEntry("openai", "personal-dev", "sk-proj-test1");
        session.AddEntry("anthropic", "claude-code", "sk-ant-test2");
        session.AddEntry("openai", "work-prod", "sk-proj-test3", expires: DateTimeOffset.UtcNow.AddDays(5)); // due!
        session.Dispose();

        var vm = CreateViewModel();
        Assert.True(vm.IsUnlocked);
        Assert.Equal(3, vm.TotalKeysCount);
        Assert.Equal(1, vm.AttentionCount);
        Assert.Equal(1, vm.ProductionCount);
        Assert.Equal(1, vm.DevelopmentCount);

        // Search text filter
        vm.SearchText = "claude";
        Assert.Single(vm.FilteredEntries);
        Assert.Equal("anthropic/claude-code", vm.FilteredEntries[0].Address);

        // Clear search
        vm.SearchText = "";
        Assert.Equal(3, vm.FilteredEntries.Count);

        // Attention filter
        vm.SelectedFilter = "attention";
        Assert.Single(vm.FilteredEntries);
        Assert.Equal("openai/work-prod", vm.FilteredEntries[0].Address);

        // Production filter
        vm.SelectedFilter = "production";
        Assert.Single(vm.FilteredEntries);
        Assert.Equal("openai/work-prod", vm.FilteredEntries[0].Address);

        // Development filter
        vm.SelectedFilter = "development";
        Assert.Single(vm.FilteredEntries);
        Assert.Equal("openai/personal-dev", vm.FilteredEntries[0].Address);

        // Provider filter
        vm.SelectedFilter = "openai";
        Assert.Equal(2, vm.FilteredEntries.Count);
    }

    [Fact]
    public void RevealAndCopy_Operations()
    {
        var createResult = VaultManager.CreateVault(_vaultPath, "test-pass", "MAIN-PC", FastKdf, _deviceStore, _stateManager);
        createResult.Session.AddEntry("openai", "test-key", "secret-value-123");
        createResult.Session.Dispose();

        var vm = CreateViewModel();
        Assert.NotNull(vm.SelectedEntry);

        Assert.False(vm.IsSecretRevealed);
        Assert.Equal("••••••••••••••••", vm.RevealedSecret);

        // Reveal
        vm.ToggleRevealSecretCommand.Execute(null);
        Assert.True(vm.IsSecretRevealed);
        Assert.Equal("secret-value-123", vm.RevealedSecret);

        // Toggle back
        vm.ToggleRevealSecretCommand.Execute(null);
        Assert.False(vm.IsSecretRevealed);
        Assert.Equal("••••••••••••••••", vm.RevealedSecret);
    }

    [Fact]
    public void AddKeyDialog_CreatesNewKeySuccessfully()
    {
        VaultManager.CreateVault(_vaultPath, "test-pass", "MAIN-PC", FastKdf, _deviceStore, _stateManager);

        var vm = CreateViewModel();
        Assert.True(vm.IsUnlocked);
        Assert.Equal(0, vm.TotalKeysCount);

        // Open Add Key Dialog
        vm.OpenAddKeyDialogCommand.Execute(null);
        Assert.True(vm.IsAddKeyDialogOpen);
        Assert.False(vm.KeyDialogIsEditing);

        // Fill out fields
        vm.KeyDialogAddress = "stripe/live";
        vm.KeyDialogSecret = "sk_live_secret999";
        vm.KeyDialogComment = "Primary billing key";
        vm.KeyDialogExpiryDays = 90;

        // Save
        vm.SaveKeyDialogCommand.Execute(null);

        Assert.False(vm.IsAddKeyDialogOpen);
        Assert.Equal(1, vm.TotalKeysCount);
        Assert.NotNull(vm.SelectedEntry);
        Assert.Equal("stripe/live", vm.SelectedEntry.Address);
        Assert.Equal("Primary billing key", vm.SelectedEntry.Comment);
    }

    [Fact]
    public void EditKeyDialog_UpdatesKeySuccessfully()
    {
        var createResult = VaultManager.CreateVault(_vaultPath, "test-pass", "MAIN-PC", FastKdf, _deviceStore, _stateManager);
        createResult.Session.AddEntry("openai", "prod", "old-secret-123", "Old comment");
        createResult.Session.Dispose();

        var vm = CreateViewModel();
        Assert.True(vm.IsUnlocked);
        Assert.NotNull(vm.SelectedEntry);

        // Open Edit Key Dialog
        vm.OpenEditKeyDialogCommand.Execute(null);
        Assert.True(vm.IsAddKeyDialogOpen);
        Assert.True(vm.KeyDialogIsEditing);
        Assert.Equal("openai/prod", vm.KeyDialogAddress);

        // Edit comment and rotate secret
        vm.KeyDialogComment = "Updated billing comment";
        vm.KeyDialogSecret = "new-rotated-secret-456";

        // Save
        vm.SaveKeyDialogCommand.Execute(null);

        Assert.False(vm.IsAddKeyDialogOpen);
        Assert.Equal("Updated billing comment", vm.SelectedEntry.Comment);
        Assert.Equal("new-rotated-secret-456", vm.SelectedEntry.Entry.Secret);
    }

    [Fact]
    public void RecentVaults_SelectionAndRemoval()
    {
        var state = _stateManager.Load();
        string vault1 = Path.Combine(_tempDir, "v1.akv");
        string vault2 = Path.Combine(_tempDir, "v2.akv");
        state.Settings.AddRecentVault(vault1);
        state.Settings.AddRecentVault(vault2);
        _stateManager.Save(state);

        var vm = CreateViewModel();
        Assert.Contains(vm.RecentVaults, r => r.FullPath == vault1);
        Assert.Contains(vm.RecentVaults, r => r.FullPath == vault2);

        // Select vault1
        var item1 = vm.RecentVaults.First(r => r.FullPath == vault1);
        vm.SelectRecentVaultCommand.Execute(item1);
        Assert.Equal(vault1, vm.TargetVaultPath);

        // Remove vault2
        var item2 = vm.RecentVaults.First(r => r.FullPath == vault2);
        vm.RemoveRecentVaultCommand.Execute(item2);
        Assert.DoesNotContain(vm.RecentVaults, r => r.FullPath == vault2);
    }

    [Fact]
    public void FirstRun_PathSynchronization()
    {
        var vm = CreateViewModel();
        vm.FirstRunDirectory = @"C:\TestDirectory";
        vm.FirstRunFileName = "custom.akv";

        Assert.Equal(@"C:\TestDirectory\custom.akv", vm.FirstRunVaultPath);
    }

    [Fact]
    public void PassphraseStrength_CalculatesScoreCorrectly()
    {
        var vm = CreateViewModel();
        vm.FirstRunPassphrase = "abc";
        Assert.Equal(1, vm.PassphraseStrengthScore);
        Assert.True(vm.IsStrengthBar1Active);
        Assert.False(vm.IsStrengthBar2Active);

        vm.FirstRunPassphrase = "P@ssw0rd123456789!SafeKeyVault";
        Assert.Equal(4, vm.PassphraseStrengthScore);
        Assert.True(vm.IsStrengthBar4Active);
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
