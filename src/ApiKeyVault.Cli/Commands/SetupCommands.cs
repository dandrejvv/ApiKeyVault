using System.ComponentModel;
using ApiKeyVault.Cli.Services;
using ApiKeyVault.Core;
using ApiKeyVault.Core.Cryptography;
using ApiKeyVault.Core.Storage;
using ApiKeyVault.Core.Vault;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ApiKeyVault.Cli.Commands;

public sealed class InitSettings : GlobalSettings
{
    [CommandOption("--path <PATH>")]
    [Description("Target path for vault.akv.")]
    public string? TargetPath { get; set; }

    [CommandOption("--device <NAME>")]
    [Description("Name for this device.")]
    public string? DeviceName { get; set; }

    [CommandOption("--fast-kdf")]
    [Description("Use fast KDF parameters (for automated testing only).")]
    public bool FastKdf { get; set; }
}

public sealed class InitCommand : Command<InitSettings>
{
    public override int Execute(CommandContext context, InitSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var stateManager = new LocalStateManager();
            string defaultPath = Path.Combine(
                Environment.GetEnvironmentVariable("OneDrive") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "ApiKeyVault",
                "vault.akv");

            string vaultPath = settings.TargetPath ?? settings.VaultPath ?? defaultPath;

            if (!settings.NoInput && settings.TargetPath == null)
            {
                vaultPath = AnsiConsole.Ask("Vault location:", vaultPath);
            }

            vaultPath = Path.GetFullPath(vaultPath);

            if (File.Exists(vaultPath) && !settings.Yes)
            {
                if (settings.NoInput)
                {
                    AnsiConsole.MarkupLine($"[red]Error:[/] Vault file already exists at '{vaultPath}'.");
                    return 2;
                }
                if (!AnsiConsole.Confirm($"Vault already exists at '{vaultPath}'. Overwrite?"))
                {
                    return 0;
                }
            }

            string passphrase;
            if (settings.PassphraseStdin)
            {
                passphrase = Console.ReadLine() ?? string.Empty;
            }
            else if (!settings.NoInput)
            {
                while (true)
                {
                    passphrase = AnsiConsole.Prompt(new TextPrompt<string>("Passphrase: ").Secret());
                    if (passphrase.Length < 8)
                    {
                        AnsiConsole.MarkupLine("[yellow]Passphrase should be at least 8 characters long.[/]");
                        continue;
                    }
                    string confirm = AnsiConsole.Prompt(new TextPrompt<string>("Confirm: ").Secret());
                    if (passphrase != confirm)
                    {
                        AnsiConsole.MarkupLine("[red]Passphrases do not match. Try again.[/]");
                        continue;
                    }
                    break;
                }
            }
            else
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Passphrase must be provided in --no-input mode via --passphrase-stdin.");
                return 2;
            }

            string deviceName = settings.DeviceName ?? Environment.MachineName;

            Argon2idParameters kdfParams;
            if (settings.FastKdf)
            {
                kdfParams = new Argon2idParameters
                {
                    MemoryKiB = CryptoConstants.MinMemoryKiB,
                    Iterations = CryptoConstants.MinIterations,
                    Parallelism = 1
                };
            }
            else
            {
                if (!settings.Quiet && !settings.NoInput)
                {
                    kdfParams = AnsiConsole.Status()
                        .Spinner(Spinner.Known.Dots)
                        .Start("Securing (tuning key derivation)...", _ => Argon2idParameters.TuneForMachine(fastMode: false));
                }
                else
                {
                    kdfParams = Argon2idParameters.TuneForMachine(fastMode: false);
                }
            }

            var result = VaultManager.CreateVault(
                vaultPath, passphrase, deviceName, kdfParams,
                DeviceKeyStoreFactory.CreateDefault(), stateManager);

            if (!settings.Json)
            {
                AnsiConsole.WriteLine();
                var panel = new Panel(
                    $"[bold white]{result.RecoveryCode}[/]\n\n" +
                    "[grey]Save it in your password manager.\nIt opens the vault if you forget the passphrase.[/]")
                {
                    Header = new PanelHeader("[yellow]Recovery code — shown once[/]"),
                    Border = BoxBorder.Rounded
                };
                AnsiConsole.Write(panel);
                AnsiConsole.WriteLine();

                // Group verification if interactive
                if (!settings.NoInput && !settings.Yes)
                {
                    string[] groups = CrockfordBase32.GetGroups(result.RecoveryCode);
                    int checkGroup = Random.Shared.Next(1, 8); // 1-indexed group 1 to 7
                    string expectedGroup = groups[checkGroup - 1];

                    while (true)
                    {
                        string entered = AnsiConsole.Ask<string>($"Type group {checkGroup} to confirm you saved it:");
                        if (string.Equals(entered.Trim(), expectedGroup, StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }
                        AnsiConsole.MarkupLine("[red]Group did not match. Please verify your recovery code.[/]");
                    }
                }

                AnsiConsole.MarkupLine($"[green]✓[/] Vault created. This device ([bold]{deviceName}[/]) is enrolled.");
            }
            else
            {
                var output = new
                {
                    vault_path = vaultPath,
                    device_name = deviceName,
                    recovery_code = result.RecoveryCode
                };
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(output));
            }

            return 0;
        }
        catch (Exception ex)
        {
            return CliContext.HandleException(ex, settings.Json);
        }
    }
}

public sealed class JoinSettings : GlobalSettings
{
    [CommandArgument(0, "[PATH]")]
    [Description("Path to the existing vault.akv file.")]
    public string? TargetPath { get; set; }

    [CommandOption("--device <NAME>")]
    [Description("Name for this device.")]
    public string? DeviceName { get; set; }
}

public sealed class JoinCommand : Command<JoinSettings>
{
    public override int Execute(CommandContext context, JoinSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var stateManager = new LocalStateManager();
            string vaultPath = settings.TargetPath ?? settings.VaultPath ?? "vault.akv";
            vaultPath = Path.GetFullPath(vaultPath);

            if (!File.Exists(vaultPath))
            {
                throw new VaultNotFoundException(vaultPath);
            }

            string passphrase;
            if (settings.PassphraseStdin)
            {
                passphrase = Console.ReadLine() ?? string.Empty;
            }
            else if (!settings.NoInput)
            {
                passphrase = AnsiConsole.Prompt(new TextPrompt<string>("Passphrase: ").Secret());
            }
            else
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Passphrase must be provided in --no-input mode.");
                return 2;
            }

            var deviceStore = DeviceKeyStoreFactory.CreateDefault();
            using var session = VaultManager.OpenWithPassphrase(
                vaultPath, passphrase, deviceStore, stateManager, settings.AcceptRollback);

            string deviceName = settings.DeviceName ?? Environment.MachineName;
            session.EnrolDevice(deviceName);

            if (!settings.Json)
            {
                AnsiConsole.MarkupLine($"[green]✓[/] Successfully joined vault. This device ([bold]{deviceName}[/]) is now enrolled.");
            }
            else
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { status = "enrolled", device = deviceName }));
            }

            return 0;
        }
        catch (Exception ex)
        {
            return CliContext.HandleException(ex, settings.Json);
        }
    }
}

public sealed class RecoverSettings : GlobalSettings
{
    [CommandOption("--code <CODE>")]
    [Description("The recovery code.")]
    public string? RecoveryCode { get; set; }

    [CommandOption("--new-passphrase <PASS>")]
    [Description("The new master passphrase.")]
    public string? NewPassphrase { get; set; }
}

public sealed class RecoverCommand : Command<RecoverSettings>
{
    public override int Execute(CommandContext context, RecoverSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var stateManager = new LocalStateManager();
            string vaultPath = CliContext.ResolveVaultPath(settings, stateManager);

            string code = settings.RecoveryCode ?? string.Empty;
            if (string.IsNullOrWhiteSpace(code) && !settings.NoInput)
            {
                code = AnsiConsole.Ask<string>("Enter recovery code:");
            }

            if (!CrockfordBase32.TryDecode(code, out _))
            {
                AnsiConsole.MarkupLine("[red]Invalid recovery code or checksum.[/]");
                return 2;
            }

            var deviceStore = DeviceKeyStoreFactory.CreateDefault();
            using var session = VaultManager.OpenWithRecovery(
                vaultPath, code, deviceStore, stateManager, settings.AcceptRollback);

            string newPass = settings.NewPassphrase ?? string.Empty;
            if (string.IsNullOrWhiteSpace(newPass) && !settings.NoInput)
            {
                while (true)
                {
                    newPass = AnsiConsole.Prompt(new TextPrompt<string>("Enter new passphrase: ").Secret());
                    string confirm = AnsiConsole.Prompt(new TextPrompt<string>("Confirm: ").Secret());
                    if (newPass == confirm) break;
                    AnsiConsole.MarkupLine("[red]Passphrases do not match. Try again.[/]");
                }
            }

            if (!string.IsNullOrWhiteSpace(newPass))
            {
                session.ChangePassphrase(newPass);
            }

            session.EnrolDevice(Environment.MachineName);

            if (!settings.Json)
            {
                AnsiConsole.MarkupLine("[green]✓[/] Vault recovered successfully. New passphrase set and this device is enrolled.");
            }
            else
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { status = "recovered" }));
            }

            return 0;
        }
        catch (Exception ex)
        {
            return CliContext.HandleException(ex, settings.Json);
        }
    }
}
