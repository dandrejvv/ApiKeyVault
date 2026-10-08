using ApiKeyVault.Core.Cryptography;
using ApiKeyVault.Core.Model;
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

        session.AddEntry("openai", "personal-dev", "demo-openai-key-dev");
        session.AddEntry("anthropic", "claude-code", "demo-anthropic-key-code");
        session.AddEntry("openai", "work-prod", "demo-openai-key-prod", expires: DateTimeOffset.UtcNow.AddDays(5)); // due!
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
        vm.KeyDialogSecret = "demo-stripe-secret-999";
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
    public void AddKeyDialog_WithCalendarExpirationDate_AndPresetShortcuts()
    {
        VaultManager.CreateVault(_vaultPath, "test-pass", "MAIN-PC", FastKdf, _deviceStore, _stateManager);

        var vm = CreateViewModel();
        vm.OpenAddKeyDialogCommand.Execute(null);

        vm.KeyDialogAddress = "github/pat";
        vm.KeyDialogSecret = "demo-github-token-998877";
        vm.KeyDialogComment = "CI Token";

        // Initially null (optional expiry)
        Assert.Null(vm.KeyDialogExpiresDate);

        // Test preset +30d
        vm.SetExpirationDaysCommand.Execute("30");
        Assert.NotNull(vm.KeyDialogExpiresDate);
        Assert.Equal(DateTime.Today.AddDays(30), vm.KeyDialogExpiresDate.Value.Date);

        // Test clear
        vm.ClearExpirationDateCommand.Execute(null);
        Assert.Null(vm.KeyDialogExpiresDate);

        // Set specific calendar date
        var targetDate = DateTime.Today.AddDays(60);
        vm.KeyDialogExpiresDate = targetDate;

        vm.SaveKeyDialogCommand.Execute(null);

        Assert.Equal(1, vm.TotalKeysCount);
        Assert.NotNull(vm.SelectedEntry);
        Assert.NotNull(vm.SelectedEntry.Entry.Expires);
        Assert.Equal(targetDate, vm.SelectedEntry.Entry.Expires.Value.LocalDateTime.Date);
    }

    [Fact]
    public void EditKeyDialog_LoadsAndClearsExpirationDate()
    {
        var createResult = VaultManager.CreateVault(_vaultPath, "test-pass", "MAIN-PC", FastKdf, _deviceStore, _stateManager);
        var initialExpiry = DateTimeOffset.UtcNow.AddDays(45);
        createResult.Session.AddEntry("slack", "bot-token", "demo-slack-token-554433", expires: initialExpiry);
        createResult.Session.Dispose();

        var vm = CreateViewModel();
        Assert.NotNull(vm.SelectedEntry);

        // Open edit dialog
        vm.OpenEditKeyDialogCommand.Execute(null);
        Assert.NotNull(vm.KeyDialogExpiresDate);
        Assert.Equal(initialExpiry.LocalDateTime.Date, vm.KeyDialogExpiresDate.Value.Date);

        // Clear expiration date
        vm.ClearExpirationDateCommand.Execute(null);
        Assert.Null(vm.KeyDialogExpiresDate);

        vm.SaveKeyDialogCommand.Execute(null);

        Assert.Null(vm.SelectedEntry.Entry.Expires);
        Assert.Null(vm.SelectedEntry.Entry.ReviewBy);
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

    [Fact]
    public void CompromisedStatus_And_Toggle_Works()
    {
        var createResult = VaultManager.CreateVault(_vaultPath, "test-pass", "MAIN-PC", FastKdf, _deviceStore, _stateManager);
        createResult.Session.AddEntry("anthropic", "intent-key", "sk-ant-test-123");
        createResult.Session.Dispose();

        var vm = CreateViewModel();
        Assert.NotNull(vm.SelectedEntry);
        Assert.False(vm.SelectedEntry.IsCompromised);
        Assert.Equal(KeyStatus.Ok, vm.SelectedEntry.Status);
        Assert.Equal(0, vm.AttentionCount);

        // Toggle Compromised
        vm.ToggleSelectedEntryCompromisedCommand.Execute(null);
        Assert.True(vm.SelectedEntry.IsCompromised);
        Assert.Equal(KeyStatus.Compromised, vm.SelectedEntry.Status);
        Assert.Equal("compromised", vm.SelectedEntry.StatusText);
        Assert.Equal(1, vm.AttentionCount);

        // Toggle off
        vm.ToggleSelectedEntryCompromisedCommand.Execute(null);
        Assert.False(vm.SelectedEntry.IsCompromised);
        Assert.Equal(KeyStatus.Ok, vm.SelectedEntry.Status);
        Assert.Equal(0, vm.AttentionCount);
    }

    [Fact]
    public void RevokedStatus_PreservesKey_And_DistinctFromDelete()
    {
        var createResult = VaultManager.CreateVault(_vaultPath, "test-pass", "MAIN-PC", FastKdf, _deviceStore, _stateManager);
        createResult.Session.AddEntry("openai", "personal-key", "sk-proj-test-456");
        createResult.Session.Dispose();

        var vm = CreateViewModel();
        Assert.NotNull(vm.SelectedEntry);
        Assert.False(vm.SelectedEntry.IsRevoked);
        Assert.Equal("Revoke Key", vm.SelectedEntry.RevokeButtonText);

        // Revoke key (does NOT delete)
        vm.ToggleSelectedEntryRevokedCommand.Execute(null);
        Assert.True(vm.SelectedEntry.IsRevoked);
        Assert.Equal(KeyStatus.Revoked, vm.SelectedEntry.Status);
        Assert.Equal("revoked", vm.SelectedEntry.StatusText);
        Assert.Equal("Restore Key", vm.SelectedEntry.RevokeButtonText);
        Assert.Equal(1, vm.TotalKeysCount); // Still preserved in vault!

        // Restore key
        vm.ToggleSelectedEntryRevokedCommand.Execute(null);
        Assert.False(vm.SelectedEntry.IsRevoked);
        Assert.Equal(KeyStatus.Ok, vm.SelectedEntry.Status);

        // Delete permanently removes it
        vm.DeleteSelectedEntryCommand.Execute(null);
        Assert.Equal(0, vm.TotalKeysCount);
        Assert.Empty(vm.FilteredEntries);
    }

    [Fact]
    public void DynamicTags_IntentAndPersonal_And_TagFiltering()
    {
        var createResult = VaultManager.CreateVault(_vaultPath, "test-pass", "MAIN-PC", FastKdf, _deviceStore, _stateManager);
        createResult.Session.AddEntry("anthropic", "dev/intent", "key-1", tags: ["intent"]);
        createResult.Session.AddEntry("gitlab", "dev/personal", "key-2", tags: ["personal"]);
        createResult.Session.Dispose();

        var vm = CreateViewModel();
        Assert.Equal(2, vm.TotalKeysCount);

        // TagFilters populated
        Assert.Contains(vm.TagFilters, t => t.Tag == "intent" && t.Count == 1);
        Assert.Contains(vm.TagFilters, t => t.Tag == "personal" && t.Count == 1);

        // Filter by tag:intent
        vm.SelectedFilter = "tag:intent";
        Assert.Single(vm.FilteredEntries);
        Assert.Equal("anthropic/dev/intent", vm.FilteredEntries[0].Address);

        // Filter by tag:personal
        vm.SelectedFilter = "tag:personal";
        Assert.Single(vm.FilteredEntries);
        Assert.Equal("gitlab/dev/personal", vm.FilteredEntries[0].Address);
    }

    [Fact]
    public void AddKeyDialog_SupportsTagsAndCompromisedFlag()
    {
        VaultManager.CreateVault(_vaultPath, "test-pass", "MAIN-PC", FastKdf, _deviceStore, _stateManager);
        var vm = CreateViewModel();

        vm.OpenAddKeyDialogCommand.Execute(null);
        vm.KeyDialogAddress = "openai/intent";
        vm.KeyDialogSecret = "sk-intent-secret-777";
        vm.KeyDialogTags = "intent, personal";
        vm.KeyDialogIsCompromised = true;

        vm.SaveKeyDialogCommand.Execute(null);

        Assert.Equal(1, vm.TotalKeysCount);
        Assert.NotNull(vm.SelectedEntry);
        Assert.True(vm.SelectedEntry.IsCompromised);
        Assert.Equal(KeyStatus.Compromised, vm.SelectedEntry.Status);
        Assert.Contains("intent", vm.SelectedEntry.Tags);
        Assert.Contains("personal", vm.SelectedEntry.Tags);
    }

    [Fact]
    public void DetectedTags_MultiSlice_BadgesAndExcludesPureNumbers()
    {
        var createResult = VaultManager.CreateVault(_vaultPath, "test-pass", "MAIN-PC", FastKdf, _deviceStore, _stateManager);
        // Multi-slice: anthropic/dev/intent with comment "#2 counter"
        createResult.Session.AddEntry("anthropic", "dev/intent", "sec-1", comment: "Testing key #2");
        createResult.Session.Dispose();

        var vm = CreateViewModel();
        Assert.Single(vm.FilteredEntries);
        var item = vm.FilteredEntries[0];

        // Should detect 'dev' and 'intent', but NOT '2'
        Assert.Contains("dev", item.Tags);
        Assert.Contains("intent", item.Tags);
        Assert.DoesNotContain("2", item.Tags);

        // Badges: intent should be sorted ahead of dev
        Assert.Equal(2, item.TagBadges.Count);
        Assert.Equal("intent", item.TagBadges[0].Name);
        Assert.Equal("#intent", item.TagBadges[0].DisplayText);
        Assert.Equal("dev", item.TagBadges[1].Name);
        Assert.Equal("#dev", item.TagBadges[1].DisplayText);

        // Visible badges
        Assert.Equal(2, item.VisibleTagBadges.Count);
        Assert.False(item.HasOverflowTags);
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
