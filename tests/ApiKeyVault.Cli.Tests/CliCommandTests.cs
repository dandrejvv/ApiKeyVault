using System.Text.Json;
using ApiKeyVault.Cli.Commands;
using ApiKeyVault.Core.Presets;
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

    // ---------- UI alignment: shared rules, tags, flags, dates ----------

    private VaultSession OpenSeedSession() =>
        VaultManager.OpenWithPassphrase(_vaultPath, "Password123!", _deviceKeyStore, new LocalStateManager());

    private static (int ExitCode, string Output) Run(Func<int> command, string stdin = "Password123!\n")
    {
        var originalOut = Console.Out;
        var output = new StringWriter();
        Console.SetIn(new StringReader(stdin));
        Console.SetOut(output);
        try
        {
            return (command(), output.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void StatusCommand_WithAttentionItems_ListsThem_IncludingCompromised()
    {
        CreateTestVault();
        using (var seed = OpenSeedSession())
        {
            seed.AddEntry("openai", "old", "sk-old", expires: DateTimeOffset.UtcNow.AddDays(-2));
            seed.AddEntry("stripe", "live", "sk-live", isCompromised: true);
            var legacy = seed.AddEntry("aws", "s3", "aws-secret");
            seed.RecordTestResult(legacy, new TestResult(false, false, null, "No automated test available", 0));
        }

        // Human-readable output used to crash on invalid markup when attention items existed.
        var text = Run(() => new StatusCommand().Execute(null!, new GlobalSettings { VaultPath = _vaultPath, PassphraseStdin = true, NoInput = true }, CancellationToken.None));
        Assert.Equal(0, text.ExitCode);

        var json = Run(() => new StatusCommand().Execute(null!, new GlobalSettings { VaultPath = _vaultPath, PassphraseStdin = true, NoInput = true, Json = true }, CancellationToken.None));
        Assert.Equal(0, json.ExitCode);
        var attention = JsonDocument.Parse(json.Output).RootElement.GetProperty("attention");
        Assert.Equal(1, attention.GetProperty("expired_count").GetInt32());
        Assert.Equal(1, attention.GetProperty("compromised_count").GetInt32());
        Assert.Equal(0, attention.GetProperty("failing_count").GetInt32());
    }

    [Fact]
    public void AddCommand_InvalidExpires_FailsWithoutSaving()
    {
        CreateTestVault();
        var settings = new AddSettings
        {
            VaultPath = _vaultPath, Provider = "openai", Name = "bad-date", Expires = "31/02/2026",
            NoTest = true, SecretStdin = true, PassphraseStdin = true, NoInput = true, Json = true
        };

        var result = Run(() => new AddCommand().Execute(null!, settings, CancellationToken.None), "Password123!\nsk-secret\n");

        Assert.Equal(2, result.ExitCode);
        using var check = OpenSeedSession();
        Assert.Empty(check.GetEntries());
    }

    [Fact]
    public void AddCommand_RelativeExpiresAndTags_AreSaved()
    {
        CreateTestVault();
        var settings = new AddSettings
        {
            VaultPath = _vaultPath, Provider = "openai", Name = "tagged", Expires = "+30d", Tags = "prod, #Intent",
            NoTest = true, SecretStdin = true, PassphraseStdin = true, NoInput = true, Json = true
        };

        var result = Run(() => new AddCommand().Execute(null!, settings, CancellationToken.None), "Password123!\nsk-secret\n");

        Assert.Equal(0, result.ExitCode);
        using var check = OpenSeedSession();
        var entry = Assert.Single(check.GetEntries());
        Assert.Equal(["prod", "intent"], entry.Tags);
        double days = (entry.Expires!.Value - DateTimeOffset.UtcNow).TotalDays;
        Assert.InRange(days, 29, 31.1);
    }

    [Fact]
    public void EditCommand_SetsFlagsAndTags_AndClearsExpiry()
    {
        CreateTestVault();
        using (var seed = OpenSeedSession())
        {
            seed.AddEntry("openai", "prod", "sk-prod", expires: DateTimeOffset.UtcNow.AddDays(10), tags: ["old"]);
        }

        var flag = new EditSettings
        {
            VaultPath = _vaultPath, KeyAddress = "openai/prod", Compromised = true, Revoked = true,
            NewTags = "", NewExpires = "none", PassphraseStdin = true, NoInput = true, Json = true
        };
        Assert.Equal(0, Run(() => new EditCommand().Execute(null!, flag, CancellationToken.None)).ExitCode);

        using (var check = OpenSeedSession())
        {
            var entry = Assert.Single(check.GetEntries());
            Assert.True(entry.IsCompromised);
            Assert.True(entry.IsRevoked);
            Assert.Empty(entry.Tags);
            Assert.Null(entry.Expires);
        }

        var unflag = new EditSettings
        {
            VaultPath = _vaultPath, KeyAddress = "openai/prod", NotCompromised = true, Restore = true,
            PassphraseStdin = true, NoInput = true, Json = true
        };
        Assert.Equal(0, Run(() => new EditCommand().Execute(null!, unflag, CancellationToken.None)).ExitCode);

        using (var check = OpenSeedSession())
        {
            var entry = Assert.Single(check.GetEntries());
            Assert.False(entry.IsCompromised);
            Assert.False(entry.IsRevoked);
        }
    }

    [Fact]
    public void EditCommand_ConflictingFlags_AreRejected()
    {
        CreateTestVault();
        var settings = new EditSettings
        {
            VaultPath = _vaultPath, KeyAddress = "openai/prod", Revoked = true, Restore = true,
            PassphraseStdin = true, NoInput = true, Json = true
        };

        Assert.Equal(2, Run(() => new EditCommand().Execute(null!, settings, CancellationToken.None)).ExitCode);
    }

    [Fact]
    public void ListCommand_FiltersByTagAndAttention_AndIgnoresLegacyCouldNotTest()
    {
        CreateTestVault();
        using (var seed = OpenSeedSession())
        {
            seed.AddEntry("openai", "prod-key", "sk-prod");
            var legacy = seed.AddEntry("aws", "s3", "aws-secret");
            seed.RecordTestResult(legacy, new TestResult(false, false, null, "No automated test available", 0));
            seed.AddEntry("stripe", "live", "sk-live", isCompromised: true);
        }

        string[] Addresses(ListSettings settings)
        {
            settings.VaultPath = _vaultPath;
            settings.PassphraseStdin = true;
            settings.NoInput = true;
            settings.Json = true;
            var result = Run(() => new ListCommand().Execute(null!, settings, CancellationToken.None));
            Assert.Equal(0, result.ExitCode);
            return JsonDocument.Parse(result.Output).RootElement.EnumerateArray()
                .Select(e => $"{e.GetProperty("provider").GetString()}/{e.GetProperty("name").GetString()}")
                .ToArray();
        }

        Assert.Equal(["openai/prod-key"], Addresses(new ListSettings { Tag = "prod" }));
        Assert.Equal(["openai/prod-key"], Addresses(new ListSettings { Search = "#prod" }));
        Assert.Empty(Addresses(new ListSettings { Failing = true }));
        Assert.Equal(["stripe/live"], Addresses(new ListSettings { Attention = true }));
    }

    [Fact]
    public void TestCommand_AllKeys_SkipsProvidersWithoutAutomatedTest()
    {
        CreateTestVault();
        using (var seed = OpenSeedSession())
        {
            seed.AddEntry("aws", "s3", "aws-secret");
        }

        var settings = new TestSettings { VaultPath = _vaultPath, PassphraseStdin = true, NoInput = true, Json = true };
        var result = Run(() => new TestCommand().Execute(null!, settings, CancellationToken.None));

        Assert.Equal(0, result.ExitCode);
        var json = JsonDocument.Parse(result.Output).RootElement;
        Assert.Equal(0, json.GetProperty("tested").GetInt32());
        Assert.Equal(1, json.GetProperty("skipped").GetInt32());

        using var check = OpenSeedSession();
        Assert.Null(Assert.Single(check.GetEntries()).LastTest);
    }
}
