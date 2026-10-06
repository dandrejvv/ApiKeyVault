using System.ComponentModel;
using System.Text.Json;
using ApiKeyVault.Cli.Services;
using ApiKeyVault.Core.Cryptography;
using ApiKeyVault.Core.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ApiKeyVault.Cli.Commands;

public sealed class AccessListCommand : Command<GlobalSettings>
{
    public override int Execute(CommandContext context, GlobalSettings settings)
    {
        try
        {
            var stateManager = new LocalStateManager();
            using var session = CliContext.OpenSession(settings, stateManager: stateManager);

            if (settings.Json)
            {
                var accessList = session.Payload.LockboxRegistry.Select(r => new
                {
                    id = r.LockboxId,
                    kind = r.Kind,
                    name = r.Name,
                    created = r.Created,
                    last_used = r.LastUsed,
                    is_current_device = r.LockboxId == session.ActiveLockboxId
                });
                Console.WriteLine(JsonSerializer.Serialize(accessList, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }

            var table = new Table().Border(TableBorder.Rounded).Title("[bold]Vault Access[/]");
            table.AddColumn("Owner");
            table.AddColumn("Added");
            table.AddColumn("Last Used");

            var now = DateTimeOffset.UtcNow;
            foreach (var r in session.Payload.LockboxRegistry)
            {
                string ownerName = Markup.Escape(r.Name);
                if (r.LockboxId == session.ActiveLockboxId)
                {
                    ownerName += " [green]★ this device[/]";
                }

                string lastUsedStr = "never";
                if (r.LastUsed.HasValue)
                {
                    int days = (int)(now - r.LastUsed.Value).TotalDays;
                    lastUsedStr = days == 0 ? "today" : $"{days} days ago";
                    if (r.Kind == "device" && days > 90)
                    {
                        lastUsedStr += " [yellow]⚠[/]";
                    }
                }

                table.AddRow(ownerName, r.Created.ToString("yyyy-MM-dd"), lastUsedStr);
            }

            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex)
        {
            return CliContext.HandleException(ex, settings.Json);
        }
    }
}

public sealed class AccessRemoveSettings : GlobalSettings
{
    [CommandArgument(0, "<DEVICES>")]
    [Description("Names or IDs of devices to remove.")]
    public string[] DeviceNames { get; set; } = [];
}

public sealed class AccessRemoveCommand : Command<AccessRemoveSettings>
{
    public override int Execute(CommandContext context, AccessRemoveSettings settings)
    {
        if (settings.DeviceNames.Length == 0)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] At least one device name or ID must be specified.");
            return 2;
        }

        try
        {
            var stateManager = new LocalStateManager();
            using var session = CliContext.OpenSession(settings, stateManager: stateManager);

            // Check if user is attempting to remove current device
            if (settings.DeviceNames.Any(d => string.Equals(d, session.CurrentDeviceName, StringComparison.OrdinalIgnoreCase) ||
                                              string.Equals(d, session.ActiveLockboxId, StringComparison.OrdinalIgnoreCase)))
            {
                AnsiConsole.MarkupLine("[red]Error:[/] You cannot remove the device you are currently using. Remove it from another device.");
                return 2;
            }

            // Check if user is attempting to remove passphrase or recovery
            if (settings.DeviceNames.Any(d => string.Equals(d, "passphrase", StringComparison.OrdinalIgnoreCase) ||
                                              string.Equals(d, "recovery", StringComparison.OrdinalIgnoreCase)))
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Passphrase and Recovery code cannot be removed; they can only be replaced.");
                return 2;
            }

            if (!settings.Yes && !settings.NoInput)
            {
                string names = string.Join(", ", settings.DeviceNames);
                AnsiConsole.MarkupLine($"[yellow]Warning:[/] {names} will lose access. The vault key will be rotated. Other devices will not be affected.");
                if (!AnsiConsole.Confirm("Proceed with removing selected devices?"))
                {
                    return 0;
                }
            }

            session.RemoveDevices(settings.DeviceNames);

            if (!settings.Json)
            {
                AnsiConsole.MarkupLine("[green]✓[/] Removed devices and rotated vault key.");
            }
            else
            {
                Console.WriteLine(JsonSerializer.Serialize(new { status = "removed", devices = settings.DeviceNames }));
            }

            return 0;
        }
        catch (Exception ex)
        {
            return CliContext.HandleException(ex, settings.Json);
        }
    }
}

public sealed class AccessRenameSettings : GlobalSettings
{
    [CommandArgument(0, "<NEW_NAME>")]
    public string NewName { get; set; } = string.Empty;
}

public sealed class AccessRenameCommand : Command<AccessRenameSettings>
{
    public override int Execute(CommandContext context, AccessRenameSettings settings)
    {
        try
        {
            var stateManager = new LocalStateManager();
            using var session = CliContext.OpenSession(settings, stateManager: stateManager);

            session.RenameDevice(settings.NewName);

            if (!settings.Json)
            {
                AnsiConsole.MarkupLine($"[green]✓[/] Renamed this device to [bold]{Markup.Escape(settings.NewName)}[/].");
            }
            else
            {
                Console.WriteLine(JsonSerializer.Serialize(new { status = "renamed", name = settings.NewName }));
            }

            return 0;
        }
        catch (Exception ex)
        {
            return CliContext.HandleException(ex, settings.Json);
        }
    }
}

public sealed class PassphraseChangeCommand : Command<GlobalSettings>
{
    public override int Execute(CommandContext context, GlobalSettings settings)
    {
        try
        {
            var stateManager = new LocalStateManager();
            using var session = CliContext.OpenSession(settings, stateManager: stateManager);

            if (settings.NoInput)
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Passphrase change cannot be performed non-interactively.");
                return 2;
            }

            // Proof required (Crypto §6.5)
            string currentPass = AnsiConsole.Prompt(new TextPrompt<string>("Current passphrase: ").Secret());
            try
            {
                // Verify proof
                using var verifySession = VaultManager.OpenWithPassphrase(
                    session.VaultPath, currentPass, new InMemoryDeviceKeyStore(), stateManager);
            }
            catch
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Current passphrase was incorrect.");
                return 3;
            }

            string newPass;
            while (true)
            {
                newPass = AnsiConsole.Prompt(new TextPrompt<string>("New passphrase: ").Secret());
                if (newPass.Length < 8)
                {
                    AnsiConsole.MarkupLine("[yellow]Passphrase should be at least 8 characters long.[/]");
                    continue;
                }
                string confirm = AnsiConsole.Prompt(new TextPrompt<string>("Confirm new passphrase: ").Secret());
                if (newPass == confirm) break;
                AnsiConsole.MarkupLine("[red]Passphrases do not match. Try again.[/]");
            }

            session.ChangePassphrase(newPass);

            AnsiConsole.MarkupLine("[green]✓[/] Passphrase changed and vault key rotated.");
            AnsiConsole.MarkupLine("[grey]Remember to update your password manager. Old backups will still open with the old passphrase.[/]");

            return 0;
        }
        catch (Exception ex)
        {
            return CliContext.HandleException(ex, settings.Json);
        }
    }
}

public sealed class RecoveryNewCommand : Command<GlobalSettings>
{
    public override int Execute(CommandContext context, GlobalSettings settings)
    {
        try
        {
            var stateManager = new LocalStateManager();
            using var session = CliContext.OpenSession(settings, stateManager: stateManager);

            if (settings.NoInput)
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Recovery code generation cannot be performed non-interactively.");
                return 2;
            }

            string currentPass = AnsiConsole.Prompt(new TextPrompt<string>("Passphrase: ").Secret());
            try
            {
                using var verify = VaultManager.OpenWithPassphrase(
                    session.VaultPath, currentPass, new InMemoryDeviceKeyStore(), stateManager);
            }
            catch
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Current passphrase was incorrect.");
                return 3;
            }

            string newCode = session.RegenerateRecoveryCode();

            var panel = new Panel($"[bold white]{newCode}[/]\n\n[grey]Save this code in your password manager.\nThe previous recovery code is now revoked.[/]")
            {
                Header = new PanelHeader("[yellow]New Recovery Code[/]"),
                Border = BoxBorder.Rounded
            };
            AnsiConsole.Write(panel);

            string[] groups = CrockfordBase32.GetGroups(newCode);
            int checkGroup = Random.Shared.Next(1, 8);
            string expectedGroup = groups[checkGroup - 1];

            while (true)
            {
                string entered = AnsiConsole.Ask<string>($"Type group {checkGroup} to confirm you saved it:");
                if (string.Equals(entered.Trim(), expectedGroup, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                AnsiConsole.MarkupLine("[red]Group did not match. Try again.[/]");
            }

            AnsiConsole.MarkupLine("[green]✓[/] New recovery code verified and vault key rotated.");
            return 0;
        }
        catch (Exception ex)
        {
            return CliContext.HandleException(ex, settings.Json);
        }
    }
}

public sealed class ConfigSettings : GlobalSettings
{
    [CommandArgument(0, "<ACTION>")]
    [Description("Action: get, set, or list.")]
    public string Action { get; set; } = "list";

    [CommandArgument(1, "[KEY]")]
    public string? Key { get; set; }

    [CommandArgument(2, "[VALUE]")]
    public string? Value { get; set; }
}

public sealed class ConfigCommand : Command<ConfigSettings>
{
    public override int Execute(CommandContext context, ConfigSettings settings)
    {
        var stateManager = new LocalStateManager();
        var state = stateManager.Load();

        string action = settings.Action.ToLowerInvariant();
        switch (action)
        {
            case "list":
                var table = new Table().Border(TableBorder.Rounded).Title("[bold]Local Settings[/]");
                table.AddColumn("Key");
                table.AddColumn("Value");

                table.AddRow("allow_secret_output_to_pipes", state.Settings.AllowSecretOutputToPipes.ToString());
                table.AddRow("clipboard_clear_seconds", state.Settings.ClipboardClearSeconds.ToString());
                table.AddRow("reveal_remask_seconds", state.Settings.RevealRemaskSeconds.ToString());
                table.AddRow("auto_lock_idle_minutes", state.Settings.AutoLockIdleMinutes.ToString());
                table.AddRow("default_vault_path", state.Settings.DefaultVaultPath ?? "(none)");

                AnsiConsole.Write(table);
                return 0;

            case "get":
                if (string.IsNullOrWhiteSpace(settings.Key))
                {
                    AnsiConsole.MarkupLine("[red]Error:[/] Setting key is required.");
                    return 2;
                }
                string val = settings.Key.ToLowerInvariant() switch
                {
                    "allow_secret_output_to_pipes" => state.Settings.AllowSecretOutputToPipes.ToString(),
                    "clipboard_clear_seconds" => state.Settings.ClipboardClearSeconds.ToString(),
                    "reveal_remask_seconds" => state.Settings.RevealRemaskSeconds.ToString(),
                    "auto_lock_idle_minutes" => state.Settings.AutoLockIdleMinutes.ToString(),
                    "default_vault_path" => state.Settings.DefaultVaultPath ?? "",
                    _ => "(unknown)"
                };
                Console.WriteLine(val);
                return 0;

            case "set":
                if (string.IsNullOrWhiteSpace(settings.Key) || settings.Value == null)
                {
                    AnsiConsole.MarkupLine("[red]Error:[/] Both key and value are required.");
                    return 2;
                }

                string k = settings.Key.ToLowerInvariant();
                if (k == "allow_secret_output_to_pipes")
                {
                    bool enable = bool.Parse(settings.Value);
                    if (enable && settings.NoInput)
                    {
                        Console.Error.WriteLine("Error: Enabling secret output to pipes requires interactive confirmation.");
                        return 2;
                    }
                    if (enable && !AnsiConsole.Confirm("Enabling secret output to pipes allows scripts to capture keys in stdout. Confirm?"))
                    {
                        return 0;
                    }
                    state.Settings.AllowSecretOutputToPipes = enable;
                }
                else if (k == "clipboard_clear_seconds")
                {
                    state.Settings.ClipboardClearSeconds = int.Parse(settings.Value);
                }
                else if (k == "default_vault_path")
                {
                    state.Settings.DefaultVaultPath = settings.Value;
                }

                stateManager.Save(state);
                AnsiConsole.MarkupLine($"[green]✓[/] Updated setting {k}.");
                return 0;

            default:
                AnsiConsole.MarkupLine($"[red]Error:[/] Unknown config action '{action}'. Use list, get, or set.");
                return 2;
        }
    }
}
