using System.ComponentModel;
using System.Text.Json;
using ApiKeyVault.Cli.Services;
using ApiKeyVault.Core.Model;
using ApiKeyVault.Core.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ApiKeyVault.Cli.Commands;

public sealed class ProfileSettings : GlobalSettings
{
    [CommandArgument(0, "<ACTION>")]
    [Description("Action: list, set, or rm.")]
    public string Action { get; set; } = "list";

    [CommandArgument(1, "[NAME]")]
    public string? ProfileName { get; set; }

    [CommandArgument(2, "[MAPPINGS]")]
    public string[]? Mappings { get; set; }
}

public sealed class ProfileCommand : Command<ProfileSettings>
{
    public override int Execute(CommandContext context, ProfileSettings settings)
    {
        try
        {
            var stateManager = new LocalStateManager();
            using var session = CliContext.OpenSession(settings, stateManager: stateManager);

            string action = settings.Action.ToLowerInvariant();
            switch (action)
            {
                case "list":
                    if (settings.Json)
                    {
                        Console.WriteLine(JsonSerializer.Serialize(session.Payload.Profiles, new JsonSerializerOptions { WriteIndented = true }));
                        return 0;
                    }

                    var table = new Table().Border(TableBorder.Rounded).Title("[bold]Run Profiles[/]");
                    table.AddColumn("Profile");
                    table.AddColumn("Mappings");

                    foreach (var p in session.Payload.Profiles)
                    {
                        var lines = p.Mappings.Select(m =>
                        {
                            var e = session.Payload.Entries.FirstOrDefault(entry => entry.Id == m.EntryId);
                            string keyAddr = e != null ? $"{e.Provider}/{e.Name}" : m.EntryId;
                            return $"{m.Var}={keyAddr}" + (!string.IsNullOrEmpty(m.Field) ? $"#{m.Field}" : "");
                        });
                        table.AddRow(Markup.Escape(p.Name), Markup.Escape(string.Join(", ", lines)));
                    }

                    AnsiConsole.Write(table);
                    return 0;

                case "set":
                    if (string.IsNullOrWhiteSpace(settings.ProfileName))
                    {
                        AnsiConsole.MarkupLine("[red]Error:[/] Profile name is required.");
                        return 2;
                    }

                    var profileMappings = new List<ProfileMapping>();
                    if (settings.Mappings != null)
                    {
                        foreach (var m in settings.Mappings)
                        {
                            int eq = m.IndexOf('=');
                            if (eq <= 0) continue;
                            string varName = m[..eq].Trim();
                            string target = m[(eq + 1)..].Trim();

                            string keyAddr = target;
                            string? field = null;
                            int hash = target.IndexOf('#');
                            if (hash > 0)
                            {
                                keyAddr = target[..hash];
                                field = target[(hash + 1)..];
                            }

                            var entry = session.FindEntry(keyAddr, allowShortName: false);
                            if (entry == null)
                            {
                                AnsiConsole.MarkupLine($"[red]Error:[/] Key '{Markup.Escape(keyAddr)}' not found.");
                                return 4;
                            }

                            profileMappings.Add(new ProfileMapping
                            {
                                Var = varName,
                                EntryId = entry.Id,
                                Field = field
                            });
                        }
                    }

                    var existing = session.Payload.Profiles.FirstOrDefault(p =>
                        string.Equals(p.Name, settings.ProfileName, StringComparison.OrdinalIgnoreCase));

                    var now = DateTimeOffset.UtcNow;
                    if (existing != null)
                    {
                        existing.Mappings = profileMappings;
                        existing.Stamp = new Stamp { Time = now, Writer = session.ActiveLockboxId };
                    }
                    else
                    {
                        session.Payload.Profiles.Add(new VaultProfile
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Name = settings.ProfileName,
                            Mappings = profileMappings,
                            Stamp = new Stamp { Time = now, Writer = session.ActiveLockboxId }
                        });
                    }

                    session.Save();
                    AnsiConsole.MarkupLine($"[green]✓[/] Profile '[bold]{Markup.Escape(settings.ProfileName)}[/]' saved.");
                    return 0;

                case "rm":
                    if (string.IsNullOrWhiteSpace(settings.ProfileName))
                    {
                        AnsiConsole.MarkupLine("[red]Error:[/] Profile name is required.");
                        return 2;
                    }

                    int removed = session.Payload.Profiles.RemoveAll(p =>
                        string.Equals(p.Name, settings.ProfileName, StringComparison.OrdinalIgnoreCase));

                    if (removed > 0)
                    {
                        session.Save();
                        AnsiConsole.MarkupLine($"[green]✓[/] Profile '[bold]{Markup.Escape(settings.ProfileName)}[/]' removed.");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[yellow]Profile '{Markup.Escape(settings.ProfileName)}' not found.[/]");
                    }
                    return 0;

                default:
                    AnsiConsole.MarkupLine($"[red]Error:[/] Unknown profile action '{action}'.");
                    return 2;
            }
        }
        catch (Exception ex)
        {
            return CliContext.HandleException(ex, settings.Json);
        }
    }
}

public sealed class ShellCommand : Command<GlobalSettings>
{
    public override int Execute(CommandContext context, GlobalSettings settings)
    {
        try
        {
            var stateManager = new LocalStateManager();
            using var session = CliContext.OpenSession(settings, stateManager: stateManager);

            AnsiConsole.MarkupLine("[bold green]ApiKeyVault Interactive Shell[/]");
            AnsiConsole.MarkupLine($"[grey]Vault: {session.VaultPath}[/]");
            AnsiConsole.MarkupLine("[grey]Type 'list', 'get <key>', 'find <text>', or 'exit' to quit.[/]\n");

            while (true)
            {
                string? input = AnsiConsole.Ask<string>("[bold blue]akv>[/]");
                if (string.IsNullOrWhiteSpace(input)) continue;

                string trimmed = input.Trim();
                if (trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals("quit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string cmd = parts[0].ToLowerInvariant();

                if (cmd == "list" || cmd == "ls")
                {
                    var listCmd = new ListCommand();
                    listCmd.Execute(context, new ListSettings { VaultPath = session.VaultPath });
                }
                else if (cmd == "find" && parts.Length > 1)
                {
                    var findCmd = new FindCommand();
                    findCmd.Execute(context, new FindSettings { VaultPath = session.VaultPath, Query = parts[1] });
                }
                else if (cmd == "get" && parts.Length > 1)
                {
                    var getCmd = new GetCommand();
                    getCmd.Execute(context, new GetSettings { VaultPath = session.VaultPath, KeyAddress = parts[1] });
                }
                else if (cmd == "status")
                {
                    var statusCmd = new StatusCommand();
                    statusCmd.Execute(context, new GlobalSettings { VaultPath = session.VaultPath });
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]Unknown command. Available: list, find, get, status, exit[/]");
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
