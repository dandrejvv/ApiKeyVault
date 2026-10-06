using System.ComponentModel;
using Spectre.Console.Cli;

namespace ApiKeyVault.Cli.Commands;

public class GlobalSettings : CommandSettings
{
    [CommandOption("--vault <PATH>")]
    [Description("Path to the vault.akv file.")]
    public string? VaultPath { get; set; }

    [CommandOption("--json")]
    [Description("Output in JSON format (never includes secrets).")]
    public bool Json { get; set; }

    [CommandOption("--no-input")]
    [Description("Never prompt for input; fail instead if input is needed.")]
    public bool NoInput { get; set; }

    [CommandOption("--yes")]
    [Description("Confirm destructive actions automatically.")]
    public bool Yes { get; set; }

    [CommandOption("--passphrase-stdin")]
    [Description("Read the unlock passphrase from the first line of stdin.")]
    public bool PassphraseStdin { get; set; }

    [CommandOption("--accept-rollback")]
    [Description("Accept an older vault version as current without prompting.")]
    public bool AcceptRollback { get; set; }

    [CommandOption("--no-color")]
    [Description("Disable color output.")]
    public bool NoColor { get; set; }

    [CommandOption("--quiet")]
    [Description("Minimal output.")]
    public bool Quiet { get; set; }
}
