namespace ApiKeyVault.UI.ViewModels;

/// <summary>
/// Monogram tile colours for providers. Brand-inspired hues only — no logos are used.
/// </summary>
public static class ProviderVisuals
{
    public static string GetDisplayName(string? provider)
    {
        if (string.IsNullOrEmpty(provider)) return "Other";
        return provider.ToLowerInvariant() switch
        {
            "openai" => "OpenAI",
            "anthropic" => "Anthropic",
            "openrouter" => "OpenRouter",
            "azure" => "Azure",
            "gemini" => "Gemini",
            "stripe" => "Stripe",
            "aws" => "AWS",
            "github" => "GitHub",
            "gitlab" => "GitLab",
            "huggingface" => "Hugging Face",
            _ => char.ToUpper(provider[0]) + provider.Substring(1)
        };
    }

    public static string GetInitial(string? provider)
    {
        var name = GetDisplayName(provider);
        return name.Length > 0 ? char.ToUpperInvariant(name[0]).ToString() : "?";
    }

    /// <summary>Returns the accent colour, tile background and tile border for a provider.</summary>
    public static (string fg, string bg, string border) GetColors(string? provider)
    {
        string hex = (provider ?? string.Empty).ToLowerInvariant() switch
        {
            "openai" => "34d399",
            "anthropic" => "e8926f",
            "openrouter" => "818cf8",
            "azure" => "38bdf8",
            "gemini" => "60a5fa",
            "stripe" => "a78bfa",
            "aws" => "fbbf24",
            "github" => "cbd5e1",
            "gitlab" => "fb923c",
            "huggingface" => "facc15",
            "clockify" => "22d3ee",
            "serper" => "f472b6",
            _ => GetDynamicHex(provider ?? string.Empty)
        };

        return ($"#{hex}", $"#1F{hex}", $"#4D{hex}");
    }

    private static string GetDynamicHex(string provider)
    {
        uint hash = 2166136261;
        foreach (char c in provider) hash = (hash ^ c) * 16777619;

        string[] palette = ["818cf8", "38bdf8", "2dd4bf", "a3e635", "f472b6", "fb7185", "c084fc"];
        return palette[hash % (uint)palette.Length];
    }
}
