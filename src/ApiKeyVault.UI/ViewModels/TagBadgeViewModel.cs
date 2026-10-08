namespace ApiKeyVault.UI.ViewModels;

public sealed class TagBadgeViewModel
{
    public string Name { get; }
    public string DisplayText { get; }
    public string ForegroundColor { get; }
    public string BackgroundColor { get; }
    public string BorderColor { get; }
    public string Tooltip { get; }

    public TagBadgeViewModel(string name)
    {
        Name = name.Trim().TrimStart('#').ToLowerInvariant();
        DisplayText = $"#{Name}";
        Tooltip = $"Tag: #{Name}";

        var colors = GetTagColors(Name);
        ForegroundColor = colors.fg;
        BackgroundColor = colors.bg;
        BorderColor = colors.border;
    }

    public static (string fg, string bg, string border) GetTagColors(string tag)
    {
        string normalized = tag.Trim().TrimStart('#').ToLowerInvariant();
        return normalized switch
        {
            "intent" => ("#c084fc", "#2a1542", "#7e22ce"),         // Vivid Purple
            "personal" => ("#22d3ee", "#0c2e3d", "#0891b2"),       // Cyan / Teal
            "dev" => ("#60a5fa", "#172554", "#2563eb"),            // Royal Blue
            "prod" or "live" => ("#34d399", "#064e3b", "#059669"),   // Emerald Green
            "staging" or "stag" => ("#fbbf24", "#3b2606", "#d97706"), // Warm Amber
            "git" => ("#fb923c", "#381c0c", "#ea580c"),            // Coral / Orange
            "embeddings" or "ai" => ("#a78bfa", "#22194d", "#6d28d9"), // Deep Violet / Indigo
            "test" or "testing" => ("#f472b6", "#381427", "#db2777"), // Pink
            "ops" or "admin" => ("#e879f9", "#34143a", "#c026d3"),   // Fuchsia
            "security" or "auth" => ("#f87171", "#371517", "#dc2626"), // Rose / Warning
            _ => GetDynamicColor(normalized)
        };
    }

    private static (string fg, string bg, string border) GetDynamicColor(string tag)
    {
        uint hash = 2166136261;
        foreach (char c in tag) hash = (hash ^ c) * 16777619;

        var palettes = new (string fg, string bg, string border)[]
        {
            ("#818cf8", "#1e1b4b", "#4338ca"), // Indigo
            ("#38bdf8", "#082f49", "#0284c7"), // Sky
            ("#2dd4bf", "#042f2e", "#0f766e"), // Teal
            ("#a3e635", "#1a2e05", "#65a30d"), // Lime
            ("#facc15", "#362b03", "#ca8a04"), // Yellow
            ("#f43f5e", "#371517", "#e11d48"), // Rose
            ("#94a3b8", "#1e293b", "#475569"), // Slate
        };
        return palettes[hash % palettes.Length];
    }
}
