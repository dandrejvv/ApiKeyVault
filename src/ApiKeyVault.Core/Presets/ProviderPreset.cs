namespace ApiKeyVault.Core.Presets;

public sealed record ProviderPreset(
    string Id,
    string DisplayName,
    string DefaultSource,
    IReadOnlyList<string> KeyPrefixes,
    IReadOnlyList<string> ExtraFields,
    string? FormatHint = null
);

public static class PresetRegistry
{
    public static readonly ProviderPreset OpenAI = new(
        Id: "openai",
        DisplayName: "OpenAI",
        DefaultSource: "https://platform.openai.com/api-keys",
        KeyPrefixes: ["sk-proj-", "sk-"],
        ExtraFields: ["organization"],
        FormatHint: "Starts with sk-proj- or sk-"
    );

    public static readonly ProviderPreset Anthropic = new(
        Id: "anthropic",
        DisplayName: "Anthropic",
        DefaultSource: "https://platform.claude.com/settings/keys",
        KeyPrefixes: ["sk-ant-api03-", "sk-ant-"],
        ExtraFields: [],
        FormatHint: "Starts with sk-ant-"
    );

    public static readonly ProviderPreset OpenRouter = new(
        Id: "openrouter",
        DisplayName: "OpenRouter",
        DefaultSource: "https://openrouter.ai/settings/keys",
        KeyPrefixes: ["sk-or-v1-", "sk-or-"],
        ExtraFields: [],
        FormatHint: "Starts with sk-or-v1-"
    );

    public static readonly ProviderPreset AzureOpenAI = new(
        Id: "azure-openai",
        DisplayName: "Azure OpenAI",
        DefaultSource: "https://portal.azure.com",
        KeyPrefixes: [],
        ExtraFields: ["endpoint", "deployment", "api_version"],
        FormatHint: "32 or 84 characters from Azure Portal"
    );

    public static readonly ProviderPreset Gemini = new(
        Id: "gemini",
        DisplayName: "Gemini",
        DefaultSource: "https://aistudio.google.com/apikey",
        KeyPrefixes: ["AIza"],
        ExtraFields: [],
        FormatHint: "39 characters starting with AIza"
    );

    public static readonly ProviderPreset GoogleStitch = new(
        Id: "google-stitch",
        DisplayName: "Google Stitch",
        DefaultSource: "https://stitch.withgoogle.com/settings",
        KeyPrefixes: [],
        ExtraFields: []
    );

    public static readonly ProviderPreset Serper = new(
        Id: "serper",
        DisplayName: "Serper",
        DefaultSource: "https://serper.dev/api-key",
        KeyPrefixes: [],
        ExtraFields: [],
        FormatHint: "40 hex characters"
    );

    public static readonly ProviderPreset Clockify = new(
        Id: "clockify",
        DisplayName: "Clockify",
        DefaultSource: "https://app.clockify.me/user/settings",
        KeyPrefixes: [],
        ExtraFields: ["workspace"],
        FormatHint: "Alphanumeric API key"
    );

    public static readonly ProviderPreset Other = new(
        Id: "other",
        DisplayName: "Other",
        DefaultSource: "",
        KeyPrefixes: [],
        ExtraFields: []
    );

    public static readonly IReadOnlyList<ProviderPreset> All =
    [
        OpenAI,
        Anthropic,
        OpenRouter,
        AzureOpenAI,
        Gemini,
        GoogleStitch,
        Serper,
        Clockify,
        Other
    ];

    public static ProviderPreset? FindById(string id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Matches provider by longest key prefix.
    /// </summary>
    public static ProviderPreset? GuessFromKey(string keySecret)
    {
        if (string.IsNullOrWhiteSpace(keySecret)) return null;

        // Longest prefix match across presets
        var candidates = All
            .SelectMany(p => p.KeyPrefixes.Select(prefix => (Preset: p, Prefix: prefix)))
            .OrderByDescending(x => x.Prefix.Length)
            .ToList();

        foreach (var (preset, prefix) in candidates)
        {
            if (keySecret.StartsWith(prefix, StringComparison.Ordinal))
            {
                return preset;
            }
        }

        return null;
    }
}
