using System.ComponentModel;
using System.Text.Json;
using ApiKeyVault.Cli.Services;
using ApiKeyVault.Core.Model;
using ApiKeyVault.Core.Presets;
using ApiKeyVault.Core.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ApiKeyVault.Cli.Commands;

public sealed class AddSettings : GlobalSettings
{
    [CommandOption("-p|--provider <PROVIDER>")]
    [Description("Key provider (openai, anthropic, azure-openai, etc.).")]
    public string? Provider { get; set; }

    [CommandOption("-n|--name <NAME>")]
    [Description("Key name.")]
    public string? Name { get; set; }

    [CommandOption("-c|--comment <COMMENT>")]
    [Description("Comment or description for the key.")]
    public string? Comment { get; set; }

    [CommandOption("-s|--source <SOURCE>")]
    [Description("Source URL for the key.")]
    public string? Source { get; set; }

    [CommandOption("--expires <DATE>")]
    [Description("Expiration date: " + DateInput.Help + ".")]
    public string? Expires { get; set; }

    [CommandOption("--review-by <DATE>")]
    [Description("Review reminder date: " + DateInput.Help + ".")]
    public string? ReviewBy { get; set; }

    [CommandOption("-t|--tags <TAGS>")]
    [Description("Comma-separated tags, e.g. \"prod, intent\".")]
    public string? Tags { get; set; }

    [CommandOption("--from-clipboard")]
    [Description("Read secret from clipboard and clear clipboard afterwards.")]
    public bool FromClipboard { get; set; }

    [CommandOption("--secret-stdin")]
    [Description("Read secret from stdin.")]
    public bool SecretStdin { get; set; }

    [CommandOption("--no-test")]
    [Description("Do not run a live test on save.")]
    public bool NoTest { get; set; }

    [CommandOption("--save-anyway")]
    [Description("Save key even if live test fails.")]
    public bool SaveAnyway { get; set; }
}

public sealed class AddCommand : Command<AddSettings>
{
    public override int Execute(CommandContext context, AddSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            DateTimeOffset? expires = null;
            if (settings.Expires != null && !DateInput.TryParse(settings.Expires, now, out expires))
            {
                return CliErrors.InvalidDate("--expires", settings.Expires, settings.Json);
            }

            DateTimeOffset? reviewBy = null;
            if (settings.ReviewBy != null && !DateInput.TryParse(settings.ReviewBy, now, out reviewBy))
            {
                return CliErrors.InvalidDate("--review-by", settings.ReviewBy, settings.Json);
            }

            var stateManager = new LocalStateManager();
            using var session = CliContext.OpenSession(settings, stateManager: stateManager);

            string provider = settings.Provider ?? string.Empty;
            if (string.IsNullOrWhiteSpace(provider) && !settings.NoInput)
            {
                var choices = PresetRegistry.All.Select(p => p.Id).ToList();
                provider = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Select [green]provider[/]:")
                        .AddChoices(choices));
            }

            string name = settings.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) && !settings.NoInput)
            {
                name = AnsiConsole.Ask<string>("Key [green]name[/]:");
            }

            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(name))
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Provider and Name are required.");
                return 2;
            }

            string secret = string.Empty;
            var clipboard = new WindowsClipboardService();

            if (settings.FromClipboard)
            {
                secret = clipboard.GetText() ?? string.Empty;
                clipboard.Clear();
                if (string.IsNullOrWhiteSpace(secret))
                {
                    AnsiConsole.MarkupLine("[red]Error:[/] Clipboard is empty.");
                    return 2;
                }
            }
            else if (settings.SecretStdin)
            {
                secret = Console.ReadLine() ?? string.Empty;
            }
            else if (!settings.NoInput)
            {
                secret = AnsiConsole.Prompt(new TextPrompt<string>("Secret: ").Secret());
            }

            if (string.IsNullOrWhiteSpace(secret))
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Secret cannot be empty.");
                return 2;
            }

            // Live test on save
            if (!settings.NoTest)
            {
                var tester = new HttpProviderTester();
                TestResult? testResult = null;

                if (!settings.Quiet && !settings.NoInput)
                {
                    testResult = AnsiConsole.Status()
                        .Spinner(Spinner.Known.Dots)
                        .Start($"Testing key against {provider}...", _ => tester.TestKeyAsync(provider, secret).GetAwaiter().GetResult());
                }
                else
                {
                    testResult = tester.TestKeyAsync(provider, secret).GetAwaiter().GetResult();
                }

                if (!testResult.Success && !testResult.CouldNotTest)
                {
                    if (settings.NoInput && !settings.SaveAnyway)
                    {
                        Console.Error.WriteLine($"Error: Live test failed: {testResult.Message}");
                        return 5;
                    }

                    if (!settings.SaveAnyway)
                    {
                        AnsiConsole.MarkupLine($"[red]✕ Live test failed:[/] {Markup.Escape(testResult.Message)}");
                        if (!AnsiConsole.Confirm("Save anyway?"))
                        {
                            return 0;
                        }
                    }
                }
                else if (testResult.Success && !settings.Quiet)
                {
                    AnsiConsole.MarkupLine($"[green]✓[/] {Markup.Escape(testResult.Message)}");
                }
            }

            var entry = session.AddEntry(
                provider: provider,
                name: name,
                secret: secret,
                comment: settings.Comment,
                source: settings.Source,
                expires: expires,
                reviewBy: reviewBy,
                tags: EntryTags.Parse(settings.Tags));

            if (!settings.Json)
            {
                AnsiConsole.MarkupLine($"[green]✓[/] Saved [bold]{entry.Provider}/{entry.Name}[/].");
            }
            else
            {
                Console.WriteLine(JsonSerializer.Serialize(new { status = "created", id = entry.Id, address = $"{entry.Provider}/{entry.Name}" }));
            }

            return 0;
        }
        catch (Exception ex)
        {
            return CliContext.HandleException(ex, settings.Json);
        }
    }
}

public sealed class EditSettings : GlobalSettings
{
    [CommandArgument(0, "<KEY>")]
    [Description("The key identifier (provider/name or id).")]
    public string KeyAddress { get; set; } = string.Empty;

    [CommandOption("-n|--name <NAME>")]
    public string? NewName { get; set; }

    [CommandOption("-c|--comment <COMMENT>")]
    public string? NewComment { get; set; }

    [CommandOption("-s|--source <SOURCE>")]
    public string? NewSource { get; set; }

    [CommandOption("--expires <DATE>")]
    [Description("New expiration date: " + DateInput.Help + ".")]
    public string? NewExpires { get; set; }

    [CommandOption("--review-by <DATE>")]
    [Description("New review reminder date: " + DateInput.Help + ".")]
    public string? NewReviewBy { get; set; }

    [CommandOption("-t|--tags <TAGS>")]
    [Description("Replace tags (comma-separated). Use \"\" to remove all tags.")]
    public string? NewTags { get; set; }

    [CommandOption("--compromised")]
    [Description("Flag the key as compromised (leaked or exposed).")]
    public bool Compromised { get; set; }

    [CommandOption("--not-compromised")]
    [Description("Clear the compromised flag.")]
    public bool NotCompromised { get; set; }

    [CommandOption("--revoked")]
    [Description("Mark the key as revoked (decommissioned, kept for reference).")]
    public bool Revoked { get; set; }

    [CommandOption("--restore")]
    [Description("Restore a revoked key to active.")]
    public bool Restore { get; set; }
}

public sealed class EditCommand : Command<EditSettings>
{
    public override int Execute(CommandContext context, EditSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            if (settings.Compromised && settings.NotCompromised)
            {
                return CliErrors.Conflict("--compromised", "--not-compromised", settings.Json);
            }
            if (settings.Revoked && settings.Restore)
            {
                return CliErrors.Conflict("--revoked", "--restore", settings.Json);
            }

            var now = DateTimeOffset.UtcNow;
            DateTimeOffset? expires = null;
            if (settings.NewExpires != null && !DateInput.TryParse(settings.NewExpires, now, out expires))
            {
                return CliErrors.InvalidDate("--expires", settings.NewExpires, settings.Json);
            }

            DateTimeOffset? reviewBy = null;
            if (settings.NewReviewBy != null && !DateInput.TryParse(settings.NewReviewBy, now, out reviewBy))
            {
                return CliErrors.InvalidDate("--review-by", settings.NewReviewBy, settings.Json);
            }

            var stateManager = new LocalStateManager();
            using var session = CliContext.OpenSession(settings, stateManager: stateManager);

            // Strict exact address only for edit
            var entry = session.FindEntry(settings.KeyAddress, allowShortName: false);
            if (entry == null)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] Key '{Markup.Escape(settings.KeyAddress)}' not found.");
                return 4;
            }

            session.EditEntry(
                settings.KeyAddress,
                newName: settings.NewName,
                newComment: settings.NewComment,
                newSource: settings.NewSource,
                newExpires: expires,
                newReviewBy: reviewBy,
                isCompromised: settings.Compromised ? true : settings.NotCompromised ? false : null,
                isRevoked: settings.Revoked ? true : settings.Restore ? false : null,
                tags: settings.NewTags != null ? EntryTags.Parse(settings.NewTags) : null,
                clearExpires: settings.NewExpires != null && expires == null,
                clearReviewBy: settings.NewReviewBy != null && reviewBy == null);

            if (!settings.Json)
            {
                AnsiConsole.MarkupLine($"[green]✓[/] Updated [bold]{entry.Provider}/{entry.Name}[/].");
            }
            else
            {
                Console.WriteLine(JsonSerializer.Serialize(new { status = "updated", address = $"{entry.Provider}/{entry.Name}" }));
            }

            return 0;
        }
        catch (Exception ex)
        {
            return CliContext.HandleException(ex, settings.Json);
        }
    }
}

public sealed class RotateSettings : GlobalSettings
{
    [CommandArgument(0, "<KEY>")]
    public string KeyAddress { get; set; } = string.Empty;

    [CommandOption("--undo")]
    [Description("Undo the last rotation and restore the previous secret.")]
    public bool Undo { get; set; }

    [CommandOption("--from-clipboard")]
    public bool FromClipboard { get; set; }

    [CommandOption("--secret-stdin")]
    public bool SecretStdin { get; set; }
}

public sealed class RotateCommand : Command<RotateSettings>
{
    public override int Execute(CommandContext context, RotateSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var stateManager = new LocalStateManager();
            using var session = CliContext.OpenSession(settings, stateManager: stateManager);

            var entry = session.FindEntry(settings.KeyAddress, allowShortName: false);
            if (entry == null)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] Key '{Markup.Escape(settings.KeyAddress)}' not found.");
                return 4;
            }

            if (settings.Undo)
            {
                session.UndoRotation(settings.KeyAddress);
                AnsiConsole.MarkupLine($"[green]✓[/] Undid rotation for [bold]{entry.Provider}/{entry.Name}[/]. Previous secret restored.");
                return 0;
            }

            string newSecret = string.Empty;
            var clipboard = new WindowsClipboardService();

            if (settings.FromClipboard)
            {
                newSecret = clipboard.GetText() ?? string.Empty;
                clipboard.Clear();
            }
            else if (settings.SecretStdin)
            {
                newSecret = Console.ReadLine() ?? string.Empty;
            }
            else if (!settings.NoInput)
            {
                newSecret = AnsiConsole.Prompt(new TextPrompt<string>("Enter new secret: ").Secret());
            }

            if (string.IsNullOrWhiteSpace(newSecret))
            {
                AnsiConsole.MarkupLine("[red]Error:[/] New secret cannot be empty.");
                return 2;
            }

            session.RotateSecret(settings.KeyAddress, newSecret);

            if (!settings.Json)
            {
                AnsiConsole.MarkupLine($"[green]✓[/] Rotated secret for [bold]{entry.Provider}/{entry.Name}[/]. Old secret kept for 7 days.");
            }
            else
            {
                Console.WriteLine(JsonSerializer.Serialize(new { status = "rotated", address = $"{entry.Provider}/{entry.Name}" }));
            }

            return 0;
        }
        catch (Exception ex)
        {
            return CliContext.HandleException(ex, settings.Json);
        }
    }
}

public sealed class RemoveSettings : GlobalSettings
{
    [CommandArgument(0, "<KEY>")]
    public string KeyAddress { get; set; } = string.Empty;
}

public sealed class RemoveCommand : Command<RemoveSettings>
{
    public override int Execute(CommandContext context, RemoveSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var stateManager = new LocalStateManager();
            using var session = CliContext.OpenSession(settings, stateManager: stateManager);

            var entry = session.FindEntry(settings.KeyAddress, allowShortName: false);
            if (entry == null)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] Key '{Markup.Escape(settings.KeyAddress)}' not found.");
                return 4;
            }

            if (!settings.Yes && !settings.NoInput)
            {
                if (!AnsiConsole.Confirm($"Delete [bold red]{entry.Provider}/{entry.Name}[/]?"))
                {
                    return 0;
                }
            }

            session.DeleteEntry(settings.KeyAddress);

            if (!settings.Json)
            {
                AnsiConsole.MarkupLine($"[green]✓[/] Deleted [bold]{entry.Provider}/{entry.Name}[/].");
            }
            else
            {
                Console.WriteLine(JsonSerializer.Serialize(new { status = "deleted", id = entry.Id }));
            }

            return 0;
        }
        catch (Exception ex)
        {
            return CliContext.HandleException(ex, settings.Json);
        }
    }
}

public sealed class TestSettings : GlobalSettings
{
    [CommandArgument(0, "[KEY]")]
    public string? KeyAddress { get; set; }

    [CommandOption("--all")]
    public bool All { get; set; }
}

public sealed class TestCommand : Command<TestSettings>
{
    public override int Execute(CommandContext context, TestSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var stateManager = new LocalStateManager();
            using var session = CliContext.OpenSession(settings, stateManager: stateManager);
            var tester = new HttpProviderTester();

            List<VaultEntry> targets;
            if (settings.All)
            {
                targets = session.GetEntries();
            }
            else if (!string.IsNullOrWhiteSpace(settings.KeyAddress))
            {
                var entry = session.FindEntry(settings.KeyAddress, allowShortName: !settings.NoInput);
                if (entry == null)
                {
                    AnsiConsole.MarkupLine($"[red]Error:[/] Key '{Markup.Escape(settings.KeyAddress)}' not found.");
                    return 4;
                }
                targets = [entry];
            }
            else
            {
                targets = session.GetEntries();
            }

            if (targets.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]No keys found to test.[/]");
                return 0;
            }

            // When testing the whole vault, keys without an automated test are skipped (the UI disables Test for them).
            bool testingAll = settings.All || string.IsNullOrWhiteSpace(settings.KeyAddress);
            int skipped = 0;
            if (testingAll)
            {
                skipped = targets.Count(t => !HttpProviderTester.IsSupported(t.Provider));
                targets = targets.Where(t => HttpProviderTester.IsSupported(t.Provider)).ToList();
            }

            bool anyFailed = false;
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("Provider");
            table.AddColumn("Name");
            table.AddColumn("Result");

            foreach (var target in targets)
            {
                var result = tester.TestKeyAsync(target.Provider, target.Secret, target.ExtraFields).GetAwaiter().GetResult();
                session.RecordTestResult(target, result);

                string resultStr;
                if (result.Success)
                {
                    resultStr = $"[green]✓ {result.ElapsedMilliseconds} ms[/]";
                }
                else if (result.CouldNotTest)
                {
                    resultStr = $"[grey]– {Markup.Escape(result.Message)}[/]";
                }
                else
                {
                    anyFailed = true;
                    resultStr = $"[red]✕ {Markup.Escape(result.Message)}[/]";
                }

                table.AddRow(Markup.Escape(target.Provider), Markup.Escape(target.Name), resultStr);
            }

            if (!settings.Json)
            {
                if (targets.Count > 0)
                {
                    AnsiConsole.Write(table);
                }
                if (skipped > 0)
                {
                    AnsiConsole.MarkupLine($"[grey]Skipped {skipped} key(s) with no automated test. Supported: {Markup.Escape(string.Join(", ", HttpProviderTester.SupportedProviders))}.[/]");
                }
            }
            else
            {
                Console.WriteLine(JsonSerializer.Serialize(new { tested = targets.Count, skipped, failed = anyFailed }));
            }

            return anyFailed ? 5 : 0;
        }
        catch (Exception ex)
        {
            return CliContext.HandleException(ex, settings.Json);
        }
    }
}

public sealed class StatusCommand : Command<GlobalSettings>
{
    public override int Execute(CommandContext context, GlobalSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var stateManager = new LocalStateManager();
            using var session = CliContext.OpenSession(settings, stateManager: stateManager);

            var now = DateTimeOffset.UtcNow;
            var entries = session.GetEntries();

            var dueEntries = entries.Where(e => EntryStatusCalculator.Compute(e, now) == KeyStatus.Due).ToList();
            var expiredEntries = entries.Where(e => EntryStatusCalculator.Compute(e, now) == KeyStatus.Expired).ToList();
            var failingEntries = entries.Where(e => EntryStatusCalculator.Compute(e, now) == KeyStatus.Failing).ToList();
            var compromisedEntries = entries.Where(e => EntryStatusCalculator.Compute(e, now) == KeyStatus.Compromised).ToList();

            var staleDevices = session.Payload.LockboxRegistry
                .Where(r => r.Kind == "device" && r.LastUsed.HasValue && (now - r.LastUsed.Value).TotalDays > 90)
                .ToList();

            if (settings.Json)
            {
                var statusObj = new
                {
                    vault_path = session.VaultPath,
                    save_counter = session.Payload.SaveCounter,
                    devices_count = session.Payload.LockboxRegistry.Count(r => r.Kind == "device"),
                    keys_count = entries.Count,
                    attention = new
                    {
                        due_count = dueEntries.Count,
                        expired_count = expiredEntries.Count,
                        failing_count = failingEntries.Count,
                        compromised_count = compromisedEntries.Count,
                        stale_devices = staleDevices.Select(d => d.Name)
                    }
                };
                Console.WriteLine(JsonSerializer.Serialize(statusObj, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }

            var table = new Table().Border(TableBorder.Rounded).Title("[bold]ApiKeyVault Status[/]");
            table.AddColumn("Property");
            table.AddColumn("Value");

            table.AddRow("Vault file", Markup.Escape(session.VaultPath));
            table.AddRow("Save counter", session.Payload.SaveCounter.ToString());
            table.AddRow("Total entries", entries.Count.ToString());
            table.AddRow("Devices", session.Payload.LockboxRegistry.Count(r => r.Kind == "device").ToString());

            AnsiConsole.Write(table);

            if (dueEntries.Count > 0 || expiredEntries.Count > 0 || failingEntries.Count > 0 || compromisedEntries.Count > 0 || staleDevices.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[yellow bold]Attention Items:[/]");
                foreach (var bad in compromisedEntries)
                {
                    AnsiConsole.MarkupLine($"  [red]⚠[/] [bold]{Markup.Escape(bad.Provider)}/{Markup.Escape(bad.Name)}[/] is marked as compromised. Rotate or revoke it.");
                }
                foreach (var exp in expiredEntries)
                {
                    AnsiConsole.MarkupLine($"  [red]▲[/] [bold]{Markup.Escape(exp.Provider)}/{Markup.Escape(exp.Name)}[/] has expired.");
                }
                foreach (var due in dueEntries)
                {
                    AnsiConsole.MarkupLine($"  [yellow]▲[/] [bold]{Markup.Escape(due.Provider)}/{Markup.Escape(due.Name)}[/] is due for review/expiry.");
                }
                foreach (var fail in failingEntries)
                {
                    AnsiConsole.MarkupLine($"  [red]✕[/] [bold]{Markup.Escape(fail.Provider)}/{Markup.Escape(fail.Name)}[/] test failing: {Markup.Escape(fail.LastTest?.Message ?? "")}");
                }
                foreach (var dev in staleDevices)
                {
                    AnsiConsole.MarkupLine($"  [yellow]⚠[/] Device [bold]{Markup.Escape(dev.Name)}[/] has not been used in > 90 days.");
                }
            }
            else
            {
                AnsiConsole.MarkupLine("[green]✓ Everything looks good! No attention items.[/]");
            }

            return 0;
        }
        catch (Exception ex)
        {
            return CliContext.HandleException(ex, settings.Json);
        }
    }
}

public sealed class ImportSettings : GlobalSettings
{
    [CommandArgument(0, "<FILE>")]
    public string FilePath { get; set; } = string.Empty;
}

public sealed class ImportCommand : Command<ImportSettings>
{
    public override int Execute(CommandContext context, ImportSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var stateManager = new LocalStateManager();
            using var session = CliContext.OpenSession(settings, stateManager: stateManager);

            if (!File.Exists(settings.FilePath))
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] File '{Markup.Escape(settings.FilePath)}' not found.");
                return 2;
            }

            string content = File.ReadAllText(settings.FilePath);
            var candidates = KeyImporter.ParseContent(content);

            if (candidates.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]No keys found in file to import.[/]");
                return 0;
            }

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("#");
            table.AddColumn("Provider");
            table.AddColumn("Name");
            table.AddColumn("Secret");

            int idx = 1;
            foreach (var c in candidates)
            {
                string masked = c.Secret.Length > 8
                    ? $"{c.Secret[..4]}...{c.Secret[^4..]}"
                    : "••••••••";
                table.AddRow(idx.ToString(), Markup.Escape(c.SuggestedProvider), Markup.Escape(c.SuggestedName), masked);
                idx++;
            }

            AnsiConsole.Write(table);

            if (!settings.Yes && !settings.NoInput)
            {
                if (!AnsiConsole.Confirm($"Import {candidates.Count} keys?"))
                {
                    return 0;
                }
            }

            int imported = 0;
            foreach (var c in candidates)
            {
                try
                {
                    session.AddEntry(c.SuggestedProvider, c.SuggestedName, c.Secret);
                    imported++;
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]Skipped {c.SuggestedProvider}/{c.SuggestedName}: {ex.Message}[/]");
                }
            }

            AnsiConsole.MarkupLine($"[green]✓[/] Imported {imported} keys.");

            if (!settings.NoInput && AnsiConsole.Confirm($"Delete source file '{Path.GetFileName(settings.FilePath)}' now?"))
            {
                File.Delete(settings.FilePath);
                AnsiConsole.MarkupLine("[grey]Source file deleted.[/]");
            }

            return 0;
        }
        catch (Exception ex)
        {
            return CliContext.HandleException(ex, settings.Json);
        }
    }
}
