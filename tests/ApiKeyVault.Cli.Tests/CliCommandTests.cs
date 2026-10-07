using System.Text.Json;
using ApiKeyVault.Cli.Commands;
using ApiKeyVault.Core.Storage;
using ApiKeyVault.Core.Vault;
using Spectre.Console.Cli;
using Xunit;

namespace ApiKeyVault.Cli.Tests;

public class CliCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _vaultPath;
    private readonly InMemoryDeviceKeyStore _deviceKeyStore;

    public CliCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "akv_cli_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _vaultPath = Path.Combine(_tempDir, "vault.akv");
        _deviceKeyStore = new InMemoryDeviceKeyStore();
        Environment.SetEnvironmentVariable("AKV_STATE_PATH", Path.Combine(_tempDir, "state.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    private void CreateTestVault(string passphrase = "Password123!")
    {
        var stateManager = new LocalStateManager();
        VaultManager.CreateVault(_vaultPath, passphrase, "TEST-DEVICE", null, _deviceKeyStore, stateManager);
    }

    [Fact]
    public void InitCommand_CreatesVault_Successfully()
    {
        var cmd = new InitCommand();
        var settings = new InitSettings
        {
            TargetPath = _vaultPath,
            DeviceName = "TEST-DEVICE",
            FastKdf = true,
            NoInput = true,
            PassphraseStdin = true,
            Json = true
        };

        using var reader = new StringReader("Password123!\n");
        Console.SetIn(reader);

        int exitCode = cmd.Execute(null!, settings, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(_vaultPath));
    }

    [Fact]
    public void AddCommand_AddsEntry_Successfully()
    {
        CreateTestVault();

        var cmd = new AddCommand();
        var settings = new AddSettings
        {
            VaultPath = _vaultPath,
            Provider = "openai",
            Name = "test-key",
            Comment = "Unit test key",
            NoTest = true,
            SecretStdin = true,
            PassphraseStdin = true,
            NoInput = true,
            Json = true
        };

        using var reader = new StringReader("Password123!\nmock-openai-cli-token-12345\n");
        Console.SetIn(reader);

        int exitCode = cmd.Execute(null!, settings, CancellationToken.None);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void ListCommand_ListsEntries_Successfully()
    {
        CreateTestVault();

        var addCmd = new AddCommand();
        var addSettings = new AddSettings
        {
            VaultPath = _vaultPath,
            Provider = "openai",
            Name = "test-key",
            NoTest = true,
            SecretStdin = true,
            PassphraseStdin = true,
            NoInput = true
        };
        using (var reader = new StringReader("Password123!\nmock-openai-cli-token-12345\n"))
        {
            Console.SetIn(reader);
            addCmd.Execute(null!, addSettings, CancellationToken.None);
        }

        var listCmd = new ListCommand();
        var listSettings = new ListSettings
        {
            VaultPath = _vaultPath,
            PassphraseStdin = true,
            NoInput = true,
            Json = true
        };

        using (var reader = new StringReader("Password123!\n"))
        {
            Console.SetIn(reader);
            int exitCode = listCmd.Execute(null!, listSettings, CancellationToken.None);
            Assert.Equal(0, exitCode);
        }
    }

    [Fact]
    public void RunCommand_InjectsEnvironmentVariables_AndRunsChild()
    {
        CreateTestVault();

        var addCmd = new AddCommand();
        var addSettings = new AddSettings
        {
            VaultPath = _vaultPath,
            Provider = "openai",
            Name = "test-key",
            NoTest = true,
            SecretStdin = true,
            PassphraseStdin = true,
            NoInput = true
        };
        using (var reader = new StringReader("Password123!\nmock-openai-cli-token-12345\n"))
        {
            Console.SetIn(reader);
            addCmd.Execute(null!, addSettings, CancellationToken.None);
        }

        var runCmd = new RunCommand();
        var runSettings = new RunSettings
        {
            VaultPath = _vaultPath,
            PassphraseStdin = true,
            NoInput = true,
            EnvVars = ["TEST_VAR=openai/test-key"],
            CommandArgs = ["dotnet", "--version"]
        };

        using (var reader = new StringReader("Password123!\n"))
        {
            Console.SetIn(reader);
            int exitCode = runCmd.Execute(null!, runSettings, CancellationToken.None);
            Assert.Equal(0, exitCode);
        }
    }

    [Fact]
    public void ConfigCommand_ListsAndSetsSettings()
    {
        var configCmd = new ConfigCommand();
        var settingsList = new ConfigSettings { Action = "list" };

        int exitCode = configCmd.Execute(null!, settingsList, CancellationToken.None);
        Assert.Equal(0, exitCode);

        var settingsSet = new ConfigSettings
        {
            Action = "set",
            Key = "clipboard_clear_seconds",
            Value = "15"
        };
        exitCode = configCmd.Execute(null!, settingsSet, CancellationToken.None);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void StatusCommand_DisplaysStatus_Successfully()
    {
        CreateTestVault();

        var statusCmd = new StatusCommand();
        var settings = new GlobalSettings
        {
            VaultPath = _vaultPath,
            PassphraseStdin = true,
            NoInput = true,
            Json = true
        };

        using var reader = new StringReader("Password123!\n");
        Console.SetIn(reader);

        int exitCode = statusCmd.Execute(null!, settings, CancellationToken.None);
        Assert.Equal(0, exitCode);
    }
}
