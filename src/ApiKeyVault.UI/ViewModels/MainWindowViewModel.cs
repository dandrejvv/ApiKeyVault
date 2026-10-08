using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ApiKeyVault.Core.Model;
using ApiKeyVault.Core.Presets;
using ApiKeyVault.Core.Storage;
using ApiKeyVault.Core.Vault;
using ApiKeyVault.UI.Services;

namespace ApiKeyVault.UI.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly IDeviceKeyStore _deviceKeyStore;
    private readonly LocalStateManager _localStateManager;
    private readonly IStorageDialogService? _storageService;
    private readonly WindowsClipboardService _clipboard;
    private DispatcherTimer? _remaskTimer;
    private DispatcherTimer? _clipboardTimer;
    private int _clipboardRemainingSeconds;

    private VaultSession? _session;

    // View Modes
    [ObservableProperty]
    private bool _isUnlocked;

    [ObservableProperty]
    private bool _isLocked = true;

    [ObservableProperty]
    private bool _isFirstRun;

    // First Run Wizard Properties
    [ObservableProperty]
    private string _firstRunDirectory = string.Empty;

    [ObservableProperty]
    private string _firstRunFileName = "vault.akv";

    [ObservableProperty]
    private string _firstRunVaultPath = string.Empty;

    [ObservableProperty]
    private string _firstRunPassphrase = string.Empty;

    [ObservableProperty]
    private string _firstRunConfirmPassphrase = string.Empty;

    [ObservableProperty]
    private int _passphraseStrengthScore;

    [ObservableProperty]
    private string _passphraseStrengthText = "Enter passphrase";

    [ObservableProperty]
    private string _passphraseStrengthColor = "#64748b";

    public bool IsStrengthBar1Active => PassphraseStrengthScore >= 1;
    public bool IsStrengthBar2Active => PassphraseStrengthScore >= 2;
    public bool IsStrengthBar3Active => PassphraseStrengthScore >= 3;
    public bool IsStrengthBar4Active => PassphraseStrengthScore >= 4;

    [ObservableProperty]
    private string? _firstRunRecoveryCode;

    [ObservableProperty]
    private string? _firstRunErrorMessage;

    [ObservableProperty]
    private bool _isCreatingVault;

    // Lock Screen & Target Vault Properties
    [ObservableProperty]
    private string _targetVaultPath = string.Empty;

    [ObservableProperty]
    private string _targetVaultFileName = string.Empty;

    [ObservableProperty]
    private string _targetVaultDirectory = string.Empty;

    [ObservableProperty]
    private string _unlockPassphrase = string.Empty;

    [ObservableProperty]
    private string? _unlockErrorMessage;

    public ObservableCollection<RecentVaultItemViewModel> RecentVaults { get; } = [];

    // Unlocked Workspace Properties
    [ObservableProperty]
    private string _activeVaultPath = string.Empty;

    public string ActiveVaultFileName => Path.GetFileName(ActiveVaultPath);

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private EntryItemViewModel? _selectedEntry;

    [ObservableProperty]
    private bool _isInspectorVisible = true;

    [RelayCommand]
    private void ToggleInspector() => IsInspectorVisible = !IsInspectorVisible;

    [RelayCommand]
    private void CloseInspector() => IsInspectorVisible = false;

    [RelayCommand]
    private void OpenInspector() => IsInspectorVisible = true;

    [ObservableProperty]
    private bool _isSecretRevealed;

    [ObservableProperty]
    private string _revealedSecret = "••••••••••••••••";

    [ObservableProperty]
    private string? _clipboardCountdownText;

    [ObservableProperty]
    private string _syncStatusText = "Ready";

    [ObservableProperty]
    private string _deviceCountText = "1 device";

    [ObservableProperty]
    private string _lastSyncTimeText = DateTimeOffset.Now.ToString("HH:mm:ss");

    [ObservableProperty]
    private int _attentionCount;

    [ObservableProperty]
    private int _productionCount;

    [ObservableProperty]
    private int _developmentCount;

    [ObservableProperty]
    private int _totalKeysCount;

    [ObservableProperty]
    private string _selectedFilter = "all";

    public ObservableCollection<EntryItemViewModel> FilteredEntries { get; } = [];
    public ObservableCollection<TagFilterItemViewModel> TagFilters { get; } = [];
    public ObservableCollection<ProviderFilterItemViewModel> ProviderFilters { get; } = [];

    // Add / Edit Key Modal Dialog
    [ObservableProperty]
    private bool _isAddKeyDialogOpen;

    [ObservableProperty]
    private string _keyDialogTitle = "Add New API Key";

    [ObservableProperty]
    private bool _keyDialogIsEditing;

    [ObservableProperty]
    private string? _keyDialogEditingId;

    [ObservableProperty]
    private string _keyDialogAddress = string.Empty;

    [ObservableProperty]
    private string _keyDialogSecret = string.Empty;

    [ObservableProperty]
    private string _keyDialogComment = string.Empty;

    [ObservableProperty]
    private string _keyDialogTags = string.Empty;

    [ObservableProperty]
    private bool _keyDialogIsCompromised;

    [ObservableProperty]
    private bool _keyDialogIsRevoked;

    [ObservableProperty]
    private DateTime? _keyDialogExpiresDate;

    public int KeyDialogExpiryDays
    {
        get => KeyDialogExpiresDate.HasValue
            ? Math.Max(0, (int)Math.Ceiling((KeyDialogExpiresDate.Value - DateTime.Today).TotalDays))
            : 0;
        set => KeyDialogExpiresDate = value > 0 ? DateTime.Today.AddDays(value) : null;
    }

    [ObservableProperty]
    private string? _keyDialogErrorMessage;

    public MainWindowViewModel(
        IDeviceKeyStore? deviceKeyStore = null,
        LocalStateManager? localStateManager = null,
        IStorageDialogService? storageService = null,
        string? defaultVaultPath = null)
    {
        _deviceKeyStore = deviceKeyStore ?? DeviceKeyStoreFactory.CreateDefault();
        _localStateManager = localStateManager ?? new LocalStateManager();
        _storageService = storageService;
        _clipboard = new WindowsClipboardService();

        InitializeState(defaultVaultPath);
    }

    private void InitializeState(string? customDefaultVaultPath = null)
    {
        var state = _localStateManager.Load();

        string defaultDir;
        string defaultFileName;
        string defaultPath;

        if (!string.IsNullOrEmpty(customDefaultVaultPath))
        {
            defaultDir = Path.GetDirectoryName(customDefaultVaultPath) ?? ".";
            defaultFileName = Path.GetFileName(customDefaultVaultPath);
            defaultPath = customDefaultVaultPath;
        }
        else
        {
            defaultDir = Path.Combine(
                Environment.GetEnvironmentVariable("OneDrive") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "ApiKeyVault");
            defaultFileName = "vault.akv";
            defaultPath = Path.Combine(defaultDir, defaultFileName);
        }

        FirstRunDirectory = defaultDir;
        FirstRunFileName = defaultFileName;
        FirstRunVaultPath = defaultPath;

        RefreshRecentVaults(state);

        string? configuredPath = state.Settings.DefaultVaultPath;
        string vaultPath = (!string.IsNullOrEmpty(configuredPath) && File.Exists(configuredPath))
            ? configuredPath
            : (RecentVaults.FirstOrDefault(r => r.Exists)?.FullPath ?? defaultPath);

        TargetVaultPath = vaultPath;

        if (!File.Exists(vaultPath))
        {
            var existingRecent = RecentVaults.FirstOrDefault(r => r.Exists);
            if (existingRecent != null)
            {
                TargetVaultPath = existingRecent.FullPath;
                IsFirstRun = false;
                IsLocked = true;
                IsUnlocked = false;
                return;
            }

            IsFirstRun = true;
            IsLocked = false;
            IsUnlocked = false;
            return;
        }

        // Try silent device unlock
        try
        {
            var session = VaultManager.OpenWithDevice(vaultPath, _deviceKeyStore, _localStateManager);
            AttachSession(session);
        }
        catch
        {
            // Lock screen
            IsFirstRun = false;
            IsLocked = true;
            IsUnlocked = false;
        }
    }

    private void RefreshRecentVaults(AppLocalState? state = null)
    {
        state ??= _localStateManager.Load();
        RecentVaults.Clear();

        foreach (var p in state.Settings.RecentVaultPaths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            bool isActive = string.Equals(p, TargetVaultPath, StringComparison.OrdinalIgnoreCase);
            RecentVaults.Add(new RecentVaultItemViewModel(p, isActive));
        }
    }

    partial void OnFirstRunDirectoryChanged(string value) => SyncFirstRunVaultPath();
    partial void OnFirstRunFileNameChanged(string value) => SyncFirstRunVaultPath();

    private void SyncFirstRunVaultPath()
    {
        string dir = string.IsNullOrWhiteSpace(FirstRunDirectory) ? "." : FirstRunDirectory.Trim();
        string file = string.IsNullOrWhiteSpace(FirstRunFileName) ? "vault.akv" : FirstRunFileName.Trim();
        FirstRunVaultPath = Path.Combine(dir, file);
    }

    partial void OnFirstRunPassphraseChanged(string value)
    {
        UpdatePassphraseStrength(value);
    }

    private void UpdatePassphraseStrength(string pass)
    {
        if (string.IsNullOrEmpty(pass))
        {
            PassphraseStrengthScore = 0;
            PassphraseStrengthText = "Enter passphrase";
            PassphraseStrengthColor = "#64748b";
        }
        else
        {
            int score = 0;
            if (pass.Length >= 8) score++;
            if (pass.Length >= 14) score++;
            if (pass.Any(char.IsDigit) && pass.Any(char.IsLetter)) score++;
            if (pass.Any(ch => !char.IsLetterOrDigit(ch))) score++;

            if (pass.Length >= 20 && score < 4) score++;

            PassphraseStrengthScore = Math.Clamp(score, 1, 4);
            (PassphraseStrengthText, PassphraseStrengthColor) = PassphraseStrengthScore switch
            {
                1 => ("Weak", "#f43f5e"),
                2 => ("Fair", "#f59e0b"),
                3 => ("Good", "#10b981"),
                4 => ("Strong (Cryptographically Secure)", "#10b981"),
                _ => ("Weak", "#f43f5e")
            };
        }

        OnPropertyChanged(nameof(IsStrengthBar1Active));
        OnPropertyChanged(nameof(IsStrengthBar2Active));
        OnPropertyChanged(nameof(IsStrengthBar3Active));
        OnPropertyChanged(nameof(IsStrengthBar4Active));
    }

    partial void OnTargetVaultPathChanged(string value)
    {
        TargetVaultFileName = Path.GetFileName(value);
        TargetVaultDirectory = Path.GetDirectoryName(value) ?? string.Empty;
        foreach (var r in RecentVaults)
        {
            r.IsActive = string.Equals(r.FullPath, value, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void AttachSession(VaultSession session)
    {
        _session = session;
        ActiveVaultPath = session.VaultPath;
        TargetVaultPath = session.VaultPath;
        IsUnlocked = true;
        IsLocked = false;
        IsFirstRun = false;
        UnlockPassphrase = string.Empty;
        UnlockErrorMessage = null;
        OnPropertyChanged(nameof(ActiveVaultFileName));

        var state = _localStateManager.Load();
        RefreshRecentVaults(state);

        UpdateEntries();
        SyncStatusText = "Encrypted & Active";
        LastSyncTimeText = DateTimeOffset.Now.ToString("HH:mm:ss");
        DeviceCountText = $"{_session.Payload.LockboxRegistry.Count(r => r.Kind == "device")} devices";
    }

    public void UpdateEntries()
    {
        if (_session == null) return;

        var entries = _session.GetEntries();
        TotalKeysCount = entries.Count;

        var now = DateTimeOffset.UtcNow;
        AttentionCount = entries.Count(e =>
        {
            var status = EntryStatusCalculator.Compute(e, now);
            return status != KeyStatus.Ok && status != KeyStatus.Revoked;
        });

        ProductionCount = entries.Count(e =>
            new EntryItemViewModel(e).Tags.Contains("prod", StringComparer.OrdinalIgnoreCase) ||
            e.Name.Contains("prod", StringComparison.OrdinalIgnoreCase) ||
            e.Name.Contains("live", StringComparison.OrdinalIgnoreCase) ||
            e.Provider.Contains("prod", StringComparison.OrdinalIgnoreCase) ||
            (e.Comment != null && (e.Comment.Contains("prod", StringComparison.OrdinalIgnoreCase) || e.Comment.Contains("live", StringComparison.OrdinalIgnoreCase))));

        DevelopmentCount = entries.Count(e =>
            new EntryItemViewModel(e).Tags.Contains("dev", StringComparer.OrdinalIgnoreCase) ||
            e.Name.Contains("dev", StringComparison.OrdinalIgnoreCase) ||
            e.Name.Contains("test", StringComparison.OrdinalIgnoreCase) ||
            e.Name.Contains("stage", StringComparison.OrdinalIgnoreCase) ||
            (e.Comment != null && (e.Comment.Contains("dev", StringComparison.OrdinalIgnoreCase) || e.Comment.Contains("test", StringComparison.OrdinalIgnoreCase))));

        // Update tag filters with counts
        var tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var itemVm = new EntryItemViewModel(entry);
            foreach (var tag in itemVm.Tags)
            {
                tagCounts[tag] = tagCounts.GetValueOrDefault(tag, 0) + 1;
            }
        }
        TagFilters.Clear();
        foreach (var kvp in tagCounts.OrderByDescending(k => k.Value).ThenBy(k => k.Key))
        {
            TagFilters.Add(new TagFilterItemViewModel(kvp.Key, kvp.Value));
        }

        // Update provider filters with counts
        var providerGroups = entries.GroupBy(e => e.Provider, StringComparer.OrdinalIgnoreCase)
                                    .OrderBy(g => g.Key)
                                    .ToList();
        ProviderFilters.Clear();
        foreach (var group in providerGroups)
        {
            ProviderFilters.Add(new ProviderFilterItemViewModel(group.Key, group.Count()));
        }

        // Filter by search & selected category
        var query = SearchText.Trim();
        var filtered = entries.AsEnumerable();

        if (SelectedFilter == "attention")
        {
            filtered = filtered.Where(e =>
            {
                var status = EntryStatusCalculator.Compute(e, now);
                return status != KeyStatus.Ok && status != KeyStatus.Revoked;
            });
        }
        else if (SelectedFilter == "revoked")
        {
            filtered = filtered.Where(e => e.IsRevoked);
        }
        else if (SelectedFilter == "production")
        {
            filtered = filtered.Where(e =>
                new EntryItemViewModel(e).Tags.Contains("prod", StringComparer.OrdinalIgnoreCase) ||
                e.Name.Contains("prod", StringComparison.OrdinalIgnoreCase) ||
                e.Name.Contains("live", StringComparison.OrdinalIgnoreCase) ||
                e.Provider.Contains("prod", StringComparison.OrdinalIgnoreCase) ||
                (e.Comment != null && (e.Comment.Contains("prod", StringComparison.OrdinalIgnoreCase) || e.Comment.Contains("live", StringComparison.OrdinalIgnoreCase))));
        }
        else if (SelectedFilter == "development")
        {
            filtered = filtered.Where(e =>
                new EntryItemViewModel(e).Tags.Contains("dev", StringComparer.OrdinalIgnoreCase) ||
                e.Name.Contains("dev", StringComparison.OrdinalIgnoreCase) ||
                e.Name.Contains("test", StringComparison.OrdinalIgnoreCase) ||
                e.Name.Contains("stage", StringComparison.OrdinalIgnoreCase) ||
                (e.Comment != null && (e.Comment.Contains("dev", StringComparison.OrdinalIgnoreCase) || e.Comment.Contains("test", StringComparison.OrdinalIgnoreCase))));
        }
        else if (SelectedFilter.StartsWith("tag:"))
        {
            var targetTag = SelectedFilter["tag:".Length..].Trim();
            filtered = filtered.Where(e => new EntryItemViewModel(e).Tags.Contains(targetTag, StringComparer.OrdinalIgnoreCase));
        }
        else if (SelectedFilter != "all")
        {
            filtered = filtered.Where(e =>
                string.Equals(e.Provider, SelectedFilter, StringComparison.OrdinalIgnoreCase) ||
                new EntryItemViewModel(e).Tags.Contains(SelectedFilter, StringComparer.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(e =>
                e.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.Provider.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (e.Comment != null && e.Comment.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                new EntryItemViewModel(e).Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        FilteredEntries.Clear();
        foreach (var e in filtered.OrderBy(e => e.Provider).ThenBy(e => e.Name))
        {
            FilteredEntries.Add(new EntryItemViewModel(e));
        }

        if (SelectedEntry == null || !FilteredEntries.Any(e => e.Id == SelectedEntry.Id))
        {
            SelectedEntry = FilteredEntries.FirstOrDefault();
        }
    }

    partial void OnSearchTextChanged(string value) => UpdateEntries();
    partial void OnSelectedFilterChanged(string value) => UpdateEntries();

    partial void OnSelectedEntryChanged(EntryItemViewModel? value)
    {
        IsSecretRevealed = false;
        RevealedSecret = "••••••••••••••••";
        if (value != null)
        {
            IsInspectorVisible = true;
        }
    }

    [RelayCommand]
    private void SetFilter(string filter)
    {
        SelectedFilter = filter;
    }

    // --- First Run Commands ---

    [RelayCommand]
    private async Task BrowseFirstRunFolderAsync()
    {
        if (_storageService == null) return;
        string? picked = await _storageService.PickFolderAsync(
            "Select Vault Storage Folder",
            FirstRunDirectory);

        if (!string.IsNullOrEmpty(picked))
        {
            FirstRunDirectory = picked;
        }
    }

    [RelayCommand]
    private void CopyTargetVaultPath()
    {
        _clipboard.SetText(FirstRunVaultPath);
        SyncStatusText = "Copied target path";
    }

    [RelayCommand]
    private async Task ChooseExistingVaultOnFirstRunAsync()
    {
        if (_storageService == null) return;
        string? picked = await _storageService.PickOpenFileAsync(
            "Open Existing ApiKeyVault",
            "ApiKeyVault (*.akv)",
            ["*.akv"],
            FirstRunDirectory);

        if (!string.IsNullOrEmpty(picked))
        {
            TargetVaultPath = picked;
            var state = _localStateManager.Load();
            state.Settings.AddRecentVault(picked);
            _localStateManager.Save(state);
            RefreshRecentVaults(state);

            IsFirstRun = false;
            IsLocked = true;
            IsUnlocked = false;
            UnlockPassphrase = string.Empty;
            UnlockErrorMessage = null;
        }
    }

    [RelayCommand]
    private void SwitchToLockScreen()
    {
        IsFirstRun = false;
        IsLocked = true;
        IsUnlocked = false;
    }

    [RelayCommand]
    private void CreateVault()
    {
        if (string.IsNullOrWhiteSpace(FirstRunPassphrase) || FirstRunPassphrase != FirstRunConfirmPassphrase)
        {
            FirstRunErrorMessage = "Passphrases do not match or are empty.";
            return;
        }

        if (string.IsNullOrWhiteSpace(FirstRunVaultPath))
        {
            FirstRunErrorMessage = "Vault path cannot be empty.";
            return;
        }

        try
        {
            var result = VaultManager.CreateVault(
                FirstRunVaultPath, FirstRunPassphrase, Environment.MachineName, null,
                _deviceKeyStore, _localStateManager);

            FirstRunRecoveryCode = result.RecoveryCode;
            AttachSession(result.Session);
        }
        catch (Exception ex)
        {
            FirstRunErrorMessage = ex.Message;
        }
    }

    // --- Lock Screen Commands ---

    [RelayCommand]
    private void SelectRecentVault(RecentVaultItemViewModel item)
    {
        TargetVaultPath = item.FullPath;
        UnlockPassphrase = string.Empty;
        UnlockErrorMessage = null;
    }

    [RelayCommand]
    private void RemoveRecentVault(RecentVaultItemViewModel item)
    {
        var state = _localStateManager.Load();
        state.Settings.RemoveRecentVault(item.FullPath);
        _localStateManager.Save(state);
        RecentVaults.Remove(item);

        if (string.Equals(TargetVaultPath, item.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            var next = RecentVaults.FirstOrDefault(r => r.Exists) ?? RecentVaults.FirstOrDefault();
            TargetVaultPath = next?.FullPath ?? FirstRunVaultPath;
        }
    }

    [RelayCommand]
    private async Task BrowseOtherVaultAsync()
    {
        if (_storageService == null) return;
        string? picked = await _storageService.PickOpenFileAsync(
            "Select Vault File",
            "ApiKeyVault (*.akv)",
            ["*.akv"],
            Path.GetDirectoryName(TargetVaultPath));

        if (!string.IsNullOrEmpty(picked))
        {
            TargetVaultPath = picked;
            var state = _localStateManager.Load();
            state.Settings.AddRecentVault(picked);
            _localStateManager.Save(state);
            RefreshRecentVaults(state);
            UnlockPassphrase = string.Empty;
            UnlockErrorMessage = null;
        }
    }

    [RelayCommand]
    private void CreateNewVaultFromLockScreen()
    {
        IsLocked = false;
        IsFirstRun = true;
        IsUnlocked = false;
        FirstRunPassphrase = string.Empty;
        FirstRunConfirmPassphrase = string.Empty;
        FirstRunErrorMessage = null;
    }

    [RelayCommand]
    private void Unlock()
    {
        if (string.IsNullOrWhiteSpace(UnlockPassphrase))
        {
            UnlockErrorMessage = "Please enter your passphrase.";
            return;
        }

        if (!File.Exists(TargetVaultPath))
        {
            UnlockErrorMessage = $"Vault file does not exist: {TargetVaultPath}";
            return;
        }

        try
        {
            var session = VaultManager.OpenWithPassphrase(TargetVaultPath, UnlockPassphrase, _deviceKeyStore, _localStateManager);
            AttachSession(session);
        }
        catch (Exception ex)
        {
            UnlockErrorMessage = ex.Message;
        }
    }

    // --- Unlocked Workspace Commands ---

    [RelayCommand]
    private void Lock()
    {
        _session?.Dispose();
        _session = null;
        IsUnlocked = false;
        IsLocked = true;
        SelectedEntry = null;
        FilteredEntries.Clear();
        UnlockPassphrase = string.Empty;
        UnlockErrorMessage = null;
    }

    [RelayCommand]
    private void SwitchVault()
    {
        _session?.Dispose();
        _session = null;
        IsUnlocked = false;
        IsFirstRun = false;
        IsLocked = true;
        SelectedEntry = null;
        FilteredEntries.Clear();
        UnlockPassphrase = string.Empty;
        UnlockErrorMessage = null;

        var state = _localStateManager.Load();
        RefreshRecentVaults(state);
    }

    [RelayCommand]
    private void CopySecret()
    {
        if (SelectedEntry == null || _session == null) return;

        string secret = SelectedEntry.Entry.Secret;
        _clipboard.SetText(secret);
        _session.RecordEntryUsed(SelectedEntry.Entry);

        StartClipboardCountdown(20);
    }

    private void StartClipboardCountdown(int seconds)
    {
        _clipboardRemainingSeconds = seconds;
        ClipboardCountdownText = $"📋 Copied · clears in {_clipboardRemainingSeconds}s";

        _clipboardTimer?.Stop();
        _clipboardTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clipboardTimer.Tick += (s, e) =>
        {
            _clipboardRemainingSeconds--;
            if (_clipboardRemainingSeconds <= 0)
            {
                _clipboardTimer?.Stop();
                _clipboard.Clear();
                ClipboardCountdownText = null;
            }
            else
            {
                ClipboardCountdownText = $"📋 Copied · clears in {_clipboardRemainingSeconds}s";
            }
        };
        _clipboardTimer.Start();
    }

    [RelayCommand]
    private void ClearClipboardNow()
    {
        _clipboardTimer?.Stop();
        _clipboard.Clear();
        ClipboardCountdownText = null;
    }

    [RelayCommand]
    private void ToggleRevealSecret()
    {
        if (SelectedEntry == null) return;

        if (IsSecretRevealed)
        {
            IsSecretRevealed = false;
            RevealedSecret = "••••••••••••••••";
            _remaskTimer?.Stop();
        }
        else
        {
            IsSecretRevealed = true;
            RevealedSecret = SelectedEntry.Entry.Secret;

            // Auto-remask after 10s
            _remaskTimer?.Stop();
            _remaskTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            _remaskTimer.Tick += (s, e) =>
            {
                _remaskTimer?.Stop();
                IsSecretRevealed = false;
                RevealedSecret = "••••••••••••••••";
            };
            _remaskTimer.Start();
        }
    }

    [RelayCommand]
    private void CopyAsCommand()
    {
        if (SelectedEntry == null) return;
        string cmd = $"akv run -e {SelectedEntry.Provider.ToUpperInvariant()}_API_KEY={SelectedEntry.Address} -- <cmd>";
        _clipboard.SetText(cmd);
        SyncStatusText = "Copied command line";
    }

    [RelayCommand]
    private async Task TestSelectedEntryAsync()
    {
        if (SelectedEntry == null || _session == null) return;

        SyncStatusText = $"Testing {SelectedEntry.Address}...";
        var tester = new HttpProviderTester();
        var result = await tester.TestKeyAsync(SelectedEntry.Provider, SelectedEntry.Entry.Secret, SelectedEntry.Entry.ExtraFields);

        _session.RecordTestResult(SelectedEntry.Entry, result);
        UpdateEntries();
        SyncStatusText = result.Success ? "Test passed" : $"Test failed: {result.Message}";
    }

    [RelayCommand]
    private void ToggleSelectedEntryCompromised()
    {
        if (SelectedEntry == null || _session == null) return;
        bool nextVal = !SelectedEntry.Entry.IsCompromised;
        _session.SetEntryCompromised(SelectedEntry.Address, nextVal);
        string currentId = SelectedEntry.Id;
        UpdateEntries();
        SelectedEntry = FilteredEntries.FirstOrDefault(e => e.Id == currentId);
        SyncStatusText = nextVal ? $"Marked {SelectedEntry?.Address} as compromised" : $"Cleared compromised status for {SelectedEntry?.Address}";
    }

    [RelayCommand]
    private void ToggleSelectedEntryRevoked()
    {
        if (SelectedEntry == null || _session == null) return;
        bool nextVal = !SelectedEntry.Entry.IsRevoked;
        _session.SetEntryRevoked(SelectedEntry.Address, nextVal);
        string currentId = SelectedEntry.Id;
        UpdateEntries();
        SelectedEntry = FilteredEntries.FirstOrDefault(e => e.Id == currentId);
        SyncStatusText = nextVal ? $"Revoked key {SelectedEntry?.Address}" : $"Restored key {SelectedEntry?.Address}";
    }

    [RelayCommand]
    private void DeleteSelectedEntry()
    {
        if (SelectedEntry == null || _session == null) return;
        string address = SelectedEntry.Address;
        _session.DeleteEntry(address);
        UpdateEntries();
        SyncStatusText = $"Permanently deleted key {address}";
    }

    [RelayCommand]
    private async Task ImportKeysAsync()
    {
        if (_session == null || _storageService == null) return;

        string? picked = await _storageService.PickOpenFileAsync(
            "Import Keys from .env or JSON",
            "Config / Env Files (*.env;*.json;*.txt)",
            ["*.env", "*.json", "*.txt"],
            Path.GetDirectoryName(ActiveVaultPath));

        if (!string.IsNullOrEmpty(picked) && File.Exists(picked))
        {
            try
            {
                string content = await File.ReadAllTextAsync(picked);
                var candidates = KeyImporter.ParseContent(content);
                int count = 0;
                foreach (var c in candidates)
                {
                    try
                    {
                        _session.AddEntry(c.SuggestedProvider, c.SuggestedName, c.Secret);
                        count++;
                    }
                    catch
                    {
                        // Duplicate or invalid key, skip
                    }
                }
                UpdateEntries();
                SyncStatusText = $"Imported {count} keys from {Path.GetFileName(picked)}";
            }
            catch (Exception ex)
            {
                SyncStatusText = $"Import failed: {ex.Message}";
            }
        }
    }

    [RelayCommand]
    private void PopulateDemoKeys()
    {
        if (_session == null) return;
        try
        {
            var today = DateTimeOffset.UtcNow;
            if (!_session.Payload.Entries.Any(e => e.Provider == "openai" && e.Name == "prod-key"))
            {
                _session.AddEntry("openai", "prod-key", "demo-openai-prod-key-12345", comment: "Primary API key", expires: today.AddDays(88));
            }
            if (!_session.Payload.Entries.Any(e => e.Provider == "stripe" && e.Name == "live-api"))
            {
                _session.AddEntry("stripe", "live-api", "demo-stripe-live-secret-67890", comment: "Production billing gateway", expires: today.AddDays(92));
            }
            if (!_session.Payload.Entries.Any(e => e.Provider == "anthropic" && e.Name == "beta-key"))
            {
                _session.AddEntry("anthropic", "beta-key", "demo-anthropic-beta-key-11223", comment: "Claude 3.5 Sonnet agent", expires: today.AddDays(3));
            }
            if (!_session.Payload.Entries.Any(e => e.Provider == "aws" && e.Name == "s3-access"))
            {
                _session.AddEntry("aws", "s3-access", "demo-aws-s3-access-key-44556", comment: "Asset storage bucket", expires: today.AddDays(180));
            }
            if (!_session.Payload.Entries.Any(e => e.Provider == "huggingface" && e.Name == "inference-api"))
            {
                _session.AddEntry("huggingface", "inference-api", "demo-hf-inference-token-77889", comment: "Open-source embedding models", expires: today.AddDays(45));
            }
            UpdateEntries();
            SelectedEntry = FilteredEntries.FirstOrDefault();
            SyncStatusText = "Loaded sample keys";
        }
        catch (Exception ex)
        {
            SyncStatusText = $"Could not load sample keys: {ex.Message}";
        }
    }

    // --- Add / Edit Key Dialog Commands ---

    [RelayCommand]
    private void OpenAddKeyDialog()
    {
        KeyDialogTitle = "Add New API Key";
        KeyDialogIsEditing = false;
        KeyDialogEditingId = null;
        KeyDialogAddress = string.Empty;
        KeyDialogSecret = string.Empty;
        KeyDialogComment = string.Empty;
        KeyDialogTags = string.Empty;
        KeyDialogIsCompromised = false;
        KeyDialogIsRevoked = false;
        KeyDialogExpiresDate = null;
        KeyDialogErrorMessage = null;
        IsAddKeyDialogOpen = true;
    }

    [RelayCommand]
    private void OpenEditKeyDialog()
    {
        if (SelectedEntry == null) return;
        KeyDialogTitle = $"Edit {SelectedEntry.Address}";
        KeyDialogIsEditing = true;
        KeyDialogEditingId = SelectedEntry.Id;
        KeyDialogAddress = SelectedEntry.Address;
        KeyDialogSecret = SelectedEntry.Entry.Secret;
        KeyDialogComment = SelectedEntry.Comment ?? string.Empty;
        KeyDialogTags = string.Join(", ", SelectedEntry.Tags.Select(t => $"#{t}"));
        KeyDialogIsCompromised = SelectedEntry.Entry.IsCompromised;
        KeyDialogIsRevoked = SelectedEntry.Entry.IsRevoked;

        if (SelectedEntry.Entry.Expires.HasValue)
        {
            KeyDialogExpiresDate = SelectedEntry.Entry.Expires.Value.LocalDateTime.Date;
        }
        else if (SelectedEntry.Entry.ReviewBy.HasValue)
        {
            KeyDialogExpiresDate = SelectedEntry.Entry.ReviewBy.Value.LocalDateTime.Date;
        }
        else
        {
            KeyDialogExpiresDate = null;
        }

        KeyDialogErrorMessage = null;
        IsAddKeyDialogOpen = true;
    }

    [RelayCommand]
    private void ClearExpirationDate()
    {
        KeyDialogExpiresDate = null;
    }

    [RelayCommand]
    private void SetExpirationDays(string daysStr)
    {
        if (int.TryParse(daysStr, out int days) && days > 0)
        {
            KeyDialogExpiresDate = DateTime.Today.AddDays(days);
        }
    }

    [RelayCommand]
    private void CancelKeyDialog()
    {
        IsAddKeyDialogOpen = false;
        KeyDialogErrorMessage = null;
    }

    [RelayCommand]
    private void SaveKeyDialog()
    {
        if (_session == null) return;

        string address = KeyDialogAddress.Trim();
        string secret = KeyDialogSecret.Trim();

        if (string.IsNullOrWhiteSpace(address))
        {
            KeyDialogErrorMessage = "Key address is required (e.g. openai/prod, stripe/live).";
            return;
        }

        if (string.IsNullOrWhiteSpace(secret))
        {
            KeyDialogErrorMessage = "Secret value cannot be empty.";
            return;
        }

        string provider;
        string name;
        if (address.Contains('/'))
        {
            var parts = address.Split('/', StringSplitOptions.RemoveEmptyEntries);
            provider = parts[0];
            name = parts.Length > 1 ? string.Join('/', parts.Skip(1)) : "default";
        }
        else
        {
            provider = "general";
            name = address;
        }

        DateTimeOffset? expires = KeyDialogExpiresDate.HasValue
            ? new DateTimeOffset(KeyDialogExpiresDate.Value.Date.AddDays(1).AddSeconds(-1), DateTimeOffset.Now.Offset)
            : null;

        var parsedTags = KeyDialogTags
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().TrimStart('#').ToLowerInvariant())
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct()
            .ToList();

        // If no explicit tags provided, infer tags from address slices
        if (parsedTags.Count == 0 && address.Contains('/'))
        {
            var slices = address.Split('/', StringSplitOptions.RemoveEmptyEntries)
                                .Select(s => s.Trim().ToLowerInvariant())
                                .ToList();
            if (slices.Count >= 2)
            {
                for (int i = 1; i < slices.Count; i++)
                {
                    if (!parsedTags.Contains(slices[i]))
                    {
                        parsedTags.Add(slices[i]);
                    }
                }
            }
        }

        try
        {
            if (KeyDialogIsEditing && !string.IsNullOrEmpty(KeyDialogEditingId))
            {
                var existing = _session.Payload.Entries.FirstOrDefault(e => e.Id == KeyDialogEditingId);
                if (existing != null)
                {
                    existing.Expires = expires;
                    existing.ReviewBy = expires;
                    existing.IsCompromised = KeyDialogIsCompromised;
                    existing.IsRevoked = KeyDialogIsRevoked;
                    existing.Tags = parsedTags;

                    _session.EditEntry(
                        existing.Id,
                        newName: name,
                        newComment: string.IsNullOrWhiteSpace(KeyDialogComment) ? null : KeyDialogComment.Trim(),
                        newExpires: expires,
                        newReviewBy: expires,
                        isCompromised: KeyDialogIsCompromised,
                        isRevoked: KeyDialogIsRevoked,
                        tags: parsedTags);

                    if (secret != existing.Secret)
                    {
                        _session.RotateSecret(existing.Id, secret);
                    }

                    UpdateEntries();
                    SelectedEntry = FilteredEntries.FirstOrDefault(e => e.Id == existing.Id);
                    IsAddKeyDialogOpen = false;
                    SyncStatusText = $"Updated {existing.Provider}/{name}";
                }
            }
            else
            {
                var added = _session.AddEntry(
                    provider: provider,
                    name: name,
                    secret: secret,
                    comment: string.IsNullOrWhiteSpace(KeyDialogComment) ? null : KeyDialogComment.Trim(),
                    expires: expires,
                    reviewBy: expires,
                    isCompromised: KeyDialogIsCompromised,
                    isRevoked: KeyDialogIsRevoked,
                    tags: parsedTags);

                UpdateEntries();
                SelectedEntry = FilteredEntries.FirstOrDefault(e => e.Id == added.Id);
                IsAddKeyDialogOpen = false;
                SyncStatusText = $"Added key {added.Provider}/{added.Name}";
            }
        }
        catch (Exception ex)
        {
            KeyDialogErrorMessage = ex.Message;
        }
    }
}
