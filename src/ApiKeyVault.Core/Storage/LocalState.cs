using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApiKeyVault.Core.Storage;

public sealed class VaultLocalState
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("highest_save_counter_seen")]
    public ulong HighestSaveCounterSeen { get; set; }

    [JsonPropertyName("revoked_padlocks_seen")]
    public List<string> RevokedPadlocksSeen { get; set; } = [];

    [JsonPropertyName("current_passphrase_lockbox_id")]
    public string? CurrentPassphraseLockboxId { get; set; }

    [JsonPropertyName("current_recovery_lockbox_id")]
    public string? CurrentRecoveryLockboxId { get; set; }

    [JsonPropertyName("last_used_throttle")]
    public Dictionary<string, DateTimeOffset> LastUsedThrottle { get; set; } = [];
}

public sealed class LocalSettings
{
    [JsonPropertyName("allow_secret_output_to_pipes")]
    public bool AllowSecretOutputToPipes { get; set; } = false;

    [JsonPropertyName("clipboard_clear_seconds")]
    public int ClipboardClearSeconds { get; set; } = 20;

    [JsonPropertyName("reveal_remask_seconds")]
    public int RevealRemaskSeconds { get; set; } = 10;

    [JsonPropertyName("auto_lock_idle_minutes")]
    public int AutoLockIdleMinutes { get; set; } = 5;

    [JsonPropertyName("lock_on_os_lock_or_sleep")]
    public bool LockOnOsLockOrSleep { get; set; } = true;

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "system";

    [JsonPropertyName("hide_from_screen_capture")]
    public bool HideFromScreenCapture { get; set; } = true;

    [JsonPropertyName("test_keys_on_save")]
    public bool TestKeysOnSave { get; set; } = true;

    [JsonPropertyName("default_vault_path")]
    public string? DefaultVaultPath { get; set; }
}

public sealed class AppLocalState
{
    [JsonPropertyName("vaults")]
    public Dictionary<string, VaultLocalState> Vaults { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("settings")]
    public LocalSettings Settings { get; set; } = new();
}

public sealed class LocalStateManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly string _stateFilePath;

    public LocalStateManager(string? customStateFilePath = null)
    {
        if (!string.IsNullOrEmpty(customStateFilePath))
        {
            _stateFilePath = customStateFilePath;
        }
        else
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appDir = Path.Combine(localAppData, "ApiKeyVault");
            _stateFilePath = Path.Combine(appDir, "state.json");
        }
    }

    public AppLocalState Load()
    {
        try
        {
            if (File.Exists(_stateFilePath))
            {
                string json = File.ReadAllText(_stateFilePath);
                return JsonSerializer.Deserialize<AppLocalState>(json, JsonOptions) ?? new AppLocalState();
            }
        }
        catch
        {
            // fallback to empty state
        }
        return new AppLocalState();
    }

    public void Save(AppLocalState state)
    {
        string? dir = Path.GetDirectoryName(_stateFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string tempPath = _stateFilePath + ".tmp." + Guid.NewGuid().ToString("N");
        string json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _stateFilePath, overwrite: true);
    }
}
