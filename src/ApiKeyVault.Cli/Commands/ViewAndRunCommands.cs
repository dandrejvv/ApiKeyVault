using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApiKeyVault.Cli.Services;
using ApiKeyVault.Core.Model;
using ApiKeyVault.Core.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ApiKeyVault.Cli.Commands;

public sealed class ListSettings : GlobalSettings
{
    [CommandOption("-p|--provider <PROVIDER>")]
    [Description("Filter entries by provider.")]
    public string? Provider { get; set; }

    [CommandOption("--due")]
    [Description("Show only entries that are due for review or expiring soon.")]
    public bool Due { get; set; }

    [CommandOption("--failing")]
    [Description("Show only entries whose last test failed.")]
    public bool Failing { get; set; }

    [CommandOption("-s|--search <TEXT>")]
    [Description("Filter by search text.")]
    public string? Search { get; set; }
}

public sealed class ListCommand : Command<ListSettings>
{
    public override int Execute(CommandContext context, ListSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var stateManager = new LocalStateManager();
            using var session = CliContext.OpenSession(settings, stateManager: stateManager);

            var entries = session.SearchEntries(
                query: settings.Search,
                provider: settings.Provider,
                dueOnly: settings.Due,
                failingOnly: settings.Failing);

            if (settings.Json)
            {
                var jsonEntries = entries.Select(e => new
                {
                    id = e.Id,
                    provider = e.Provider,
                    name = e.Name,
                    comment = e.Comment,
                    source = e.Source,
                    expires = e.Expires,
                    review_by = e.ReviewBy,
                    status = EntryStatusCalculator.Compute(e, DateTimeOffset.UtcNow).ToString().ToLowerInvariant(),
                    created = e.Created,
                    last_used = e.LastUsed,
                    last_test = e.LastTest != null ? new
                    {
                        time = e.LastTest.Time,
                        success = e.LastTest.Success,
                        status_code = e.LastTest.StatusCode,
                        message = e.LastTest.Message
                    } : null
                });
                Console.WriteLine(JsonSerializer.Serialize(jsonEntries, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("Status");
            table.AddColumn("Provider");
            table.AddColumn("Name");
            table.AddColumn("Expires / Review");
            table.AddColumn("Last Test");
            table.AddColumn("Comment");

            var now = DateTimeOffset.UtcNow;
            foreach (var e in entries)
            {
                var status = EntryStatusCalculator.Compute(e, now);
                string statusMarkup = status switch
                {
                    KeyStatus.Ok => "[green]●[/]",
                    KeyStatus.Due => "[yellow]▲[/]",
                    KeyStatus.Expired => "[red]▲[/]",
                    KeyStatus.Failing => "[red]✕[/]",
                    _ => "[grey]?[/]"
                };

                string expStr = "—";
                if (e.Expires.HasValue)
                {
                    int days = (int)Math.Ceiling((e.Expires.Value - now).TotalDays);
                    expStr = days >= 0 ? $"exp in {days}d" : $"expired {Math.Abs(days)}d ago";
                }
                else if (e.ReviewBy.HasValue)
                {
                    int days = (int)Math.Ceiling((e.ReviewBy.Value - now).TotalDays);
                    expStr = days >= 0 ? $"rev in {days}d" : $"review due";
                }

                string testStr = "—";
                if (e.LastTest != null)
                {
                    testStr = e.LastTest.Success
                        ? "[green]✓[/]"
                        : $"[red]✕ {e.LastTest.StatusCode}[/]";
                }

                table.AddRow(
                    statusMarkup,
                    Markup.Escape(e.Provider),
                    Markup.Escape(e.Name),
                    Markup.Escape(expStr),
                    testStr,
                    Markup.Escape(e.Comment ?? "")
                );
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

public sealed class FindSettings : GlobalSettings
{
    [CommandArgument(0, "<TEXT>")]
    [Description("Search query text.")]
    public string Query { get; set; } = string.Empty;
}

public sealed class FindCommand : Command<FindSettings>
{
    public override int Execute(CommandContext context, FindSettings settings, CancellationToken cancellationToken)
    {
        var listSettings = new ListSettings
        {
            VaultPath = settings.VaultPath,
            Json = settings.Json,
            NoInput = settings.NoInput,
            Search = settings.Query
        };
        return new ListCommand().Execute(context, listSettings, cancellationToken);
    }
}

public sealed class GetSettings : GlobalSettings
{
    [CommandArgument(0, "<KEY>")]
    [Description("The key identifier (provider/name, name, or id).")]
    public string KeyAddress { get; set; } = string.Empty;

    [CommandOption("--stdout")]
    [Description("Output secret directly to stdout.")]
    public bool Stdout { get; set; }

    [CommandOption("--newline")]
    [Description("Add newline to stdout output.")]
    public bool Newline { get; set; }

    [CommandOption("--reveal")]
    [Description("Display the secret on screen.")]
    public bool Reveal { get; set; }

    [CommandOption("--no-wait")]
    [Description("Do not wait for clipboard auto-clear countdown.")]
    public bool NoWait { get; set; }
}

public sealed class GetCommand : Command<GetSettings>
{
    public override int Execute(CommandContext context, GetSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var stateManager = new LocalStateManager();
            var state = stateManager.Load();
            using var session = CliContext.OpenSession(settings, stateManager: stateManager);

            bool allowShort = !settings.Stdout && !settings.NoInput && !Console.IsOutputRedirected;
            var entry = session.FindEntry(settings.KeyAddress, allowShortName: allowShort);

            if (entry == null)
            {
                if (settings.Json)
                {
                    Console.Error.WriteLine(JsonSerializer.Serialize(new { error = $"Key '{settings.KeyAddress}' not found.", exit_code = 4 }));
                }
                else
                {
                    AnsiConsole.MarkupLine($"[red]Error:[/] Key '{Markup.Escape(settings.KeyAddress)}' not found.");
                }
                return 4;
            }

            session.RecordEntryUsed(entry);
            string secret = entry.Secret;

            // 1. --stdout mode
            if (settings.Stdout)
            {
                if (!Console.IsOutputRedirected)
                {
                    AnsiConsole.MarkupLine("[red]Error:[/] --stdout cannot be used when stdout is connected to a terminal. Use --reveal instead.");
                    return 2;
                }

                if (!state.Settings.AllowSecretOutputToPipes)
                {
                    Console.Error.WriteLine("Error: Secret output to pipes is disabled on this device. Use 'akv run' instead.");
                    return 2;
                }

                if (settings.Newline)
                {
                    Console.WriteLine(secret);
                }
                else
                {
                    Console.Write(secret);
                }
                return 0;
            }

            // 2. --reveal mode
            if (settings.Reveal)
            {
                if (Console.IsOutputRedirected)
                {
                    Console.Error.WriteLine("Error: --reveal cannot be used when output is redirected.");
                    return 2;
                }

                AnsiConsole.WriteLine();
                var panel = new Panel($"[bold white]{Markup.Escape(secret)}[/]")
                {
                    Header = new PanelHeader($"[yellow]Secret for {entry.Provider}/{entry.Name}[/]"),
                    Border = BoxBorder.Rounded
                };
                AnsiConsole.Write(panel);
                AnsiConsole.MarkupLine("[grey]Warning: Secret will remain in terminal scrollback buffer.[/]");
                return 0;
            }

            // 3. Clipboard mode (default)
            var clipboard = new WindowsClipboardService();
            clipboard.SetText(secret);
            byte[] secretHash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));

            if (settings.NoWait)
            {
                if (!settings.Quiet)
                {
                    AnsiConsole.MarkupLine($"[green]✓[/] Copied [bold]{entry.Provider}/{entry.Name}[/] to clipboard.");
                }
                return 0;
            }

            int timeoutSeconds = state.Settings.ClipboardClearSeconds;
            if (timeoutSeconds <= 0) timeoutSeconds = 20;

            if (!settings.Quiet && !Console.IsOutputRedirected)
            {
                AnsiConsole.MarkupLine($"[green]✓[/] Copied [bold]{entry.Provider}/{entry.Name}[/] to clipboard.");
                AnsiConsole.Progress()
                    .AutoClear(true)
                    .Columns([new TaskDescriptionColumn(), new ProgressBarColumn(), new RemainingTimeColumn()])
                    .Start(ctx =>
                    {
                        var task = ctx.AddTask($"Clearing clipboard in {timeoutSeconds}s", maxValue: timeoutSeconds);
                        for (int i = 0; i < timeoutSeconds; i++)
                        {
                            Thread.Sleep(1000);
                            task.Increment(1);
                        }
                    });

                if (clipboard.MatchesCurrent(secretHash))
                {
                    clipboard.Clear();
                    AnsiConsole.MarkupLine("[grey]Clipboard cleared.[/]");
                }
            }
            else
            {
                Thread.Sleep(timeoutSeconds * 1000);
                if (clipboard.MatchesCurrent(secretHash))
                {
                    clipboard.Clear();
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            return CliContext.HandleException(ex, settings.Json);
        }
    }
}

public sealed class RunSettings : GlobalSettings
{
    [CommandOption("-e|--env <ENV>")]
    [Description("Environment variable mapping: VAR=provider/name[#field].")]
    public string[]? EnvVars { get; set; }

    [CommandOption("--profile <NAME>")]
    [Description("Run profile name.")]
    public string? Profile { get; set; }

    [CommandArgument(0, "<COMMAND>")]
    [Description("The command line to run.")]
    public string[] CommandArgs { get; set; } = [];
}

public sealed class RunCommand : Command<RunSettings>
{
    public override int Execute(CommandContext context, RunSettings settings, CancellationToken cancellationToken)
    {
        if (settings.CommandArgs.Length == 0)
        {
            Console.Error.WriteLine("Error: No command specified to run.");
            return 125;
        }

        try
        {
            var stateManager = new LocalStateManager();
            using var session = CliContext.OpenSession(settings, stateManager: stateManager);

            var envMappings = new Dictionary<string, string>();

            // Load profile if specified
            if (!string.IsNullOrWhiteSpace(settings.Profile))
            {
                var prof = session.Payload.Profiles.FirstOrDefault(p =>
                    string.Equals(p.Name, settings.Profile, StringComparison.OrdinalIgnoreCase));
                if (prof == null)
                {
                    Console.Error.WriteLine($"Error: Profile '{settings.Profile}' not found.");
                    return 125;
                }

                foreach (var mapping in prof.Mappings)
                {
                    var entry = session.Payload.Entries.FirstOrDefault(e => e.Id == mapping.EntryId);
                    if (entry == null)
                    {
                        Console.Error.WriteLine($"Error: Profile entry '{mapping.EntryId}' not found.");
                        return 125;
                    }
                    string val = entry.Secret;
                    if (!string.IsNullOrEmpty(mapping.Field))
                    {
                        entry.ExtraFields?.TryGetValue(mapping.Field, out val!);
                    }
                    envMappings[mapping.Var] = val ?? "";
                }
            }

            // Load explicit -e mappings
            if (settings.EnvVars != null)
            {
                foreach (var envStr in settings.EnvVars)
                {
                    int eqIndex = envStr.IndexOf('=');
                    if (eqIndex <= 0)
                    {
                        Console.Error.WriteLine($"Error: Invalid env mapping format: '{envStr}'. Expected VAR=provider/name.");
                        return 125;
                    }

                    string varName = envStr[..eqIndex].Trim();
                    string target = envStr[(eqIndex + 1)..].Trim();

                    string keyAddress = target;
                    string? field = null;
                    int hashIndex = target.IndexOf('#');
                    if (hashIndex > 0)
                    {
                        keyAddress = target[..hashIndex];
                        field = target[(hashIndex + 1)..];
                    }

                    // Strict requirement: exact address only for run command!
                    var entry = session.FindEntry(keyAddress, allowShortName: false);
                    if (entry == null)
                    {
                        Console.Error.WriteLine($"Error: Key '{keyAddress}' not found. Exact 'provider/name' or id required.");
                        return 125;
                    }

                    session.RecordEntryUsed(entry);

                    string value = entry.Secret;
                    if (!string.IsNullOrEmpty(field))
                    {
                        if (entry.ExtraFields?.TryGetValue(field, out var extraVal) == true)
                        {
                            value = extraVal;
                        }
                        else
                        {
                            Console.Error.WriteLine($"Error: Extra field '{field}' not found on key '{keyAddress}'.");
                            return 125;
                        }
                    }

                    envMappings[varName] = value;
                }
            }

            // Launch child process
            string fileName = settings.CommandArgs[0];
            string[] rawArgs = settings.CommandArgs.Skip(1).ToArray();

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false
            };

            foreach (var arg in rawArgs)
            {
                psi.ArgumentList.Add(arg);
            }

            foreach (var (k, v) in envMappings)
            {
                psi.Environment[k] = v;
            }

            Process? process;
            try
            {
                process = Process.Start(psi);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2) // File not found
            {
                Console.Error.WriteLine($"Error: Command '{fileName}' not found.");
                return 127;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: Cannot execute '{fileName}': {ex.Message}");
                return 126;
            }

            if (process == null)
            {
                Console.Error.WriteLine($"Error: Failed to start '{fileName}'.");
                return 126;
            }

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 125;
        }
    }
}
