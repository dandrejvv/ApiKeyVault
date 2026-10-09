namespace ApiKeyVault.Cli.Services;

/// <summary>Usage errors reported the same way as other CLI failures (exit code 2).</summary>
public static class CliErrors
{
    public static int InvalidDate(string option, string value, bool json) =>
        CliContext.HandleException(new ArgumentException($"Invalid {option} value '{value}'. Use {DateInput.Help}."), json);

    public static int Conflict(string first, string second, bool json) =>
        CliContext.HandleException(new ArgumentException($"{first} and {second} can't be used together."), json);
}
