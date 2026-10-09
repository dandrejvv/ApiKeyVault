namespace ApiKeyVault.Core.Model;

/// <summary>
/// Tag rules shared by the CLI and the desktop UI.
/// </summary>
public static class EntryTags
{
    private static readonly string[] KnownCategoryKeywords =
    [
        "intent", "personal", "prod", "production", "dev", "development",
        "staging", "stag", "test", "testing", "live", "beta", "git",
        "embeddings", "ai", "qa", "sandbox", "demo", "admin"
    ];

    /// <summary>
    /// Parses user-typed tags ("prod, #Intent dev") into normalised, distinct tag names.
    /// </summary>
    public static List<string> Parse(string? input) =>
        (input ?? string.Empty)
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().TrimStart('#').ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();

    /// <summary>
    /// All tags for an entry: explicit tags, scope slices of a multi-part address
    /// (provider/env/team/name), recognised keywords in the name, and #hashtags in the comment.
    /// </summary>
    public static IReadOnlyList<string> Derive(VaultEntry entry)
    {
        var list = new List<string>();

        void AddTag(string tag)
        {
            if (tag.Length > 0 && tag.Any(char.IsLetter) && !list.Contains(tag)) list.Add(tag);
        }

        // 1. Explicit tags saved on the entry
        foreach (var t in entry.Tags ?? [])
        {
            AddTag(t.Trim().TrimStart('#').ToLowerInvariant());
        }

        // 2. Multi-slice path parsing from the address (e.g. provider/slice1/slice2/name)
        var slices = $"{entry.Provider}/{entry.Name}"
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().ToLowerInvariant())
            .ToList();

        if (slices.Count >= 3)
        {
            // Intermediate slices [1..^1] are hierarchical scope tags
            for (int i = 1; i < slices.Count - 1; i++) AddTag(slices[i]);

            // If the last slice is also a recognised keyword (e.g. 'intent', 'personal'), include it
            if (IsCategoryKeyword(slices[^1])) AddTag(slices[^1]);
        }
        else if (slices.Count == 2)
        {
            // In a standard provider/name address, only recognised keywords or prefixes/suffixes count
            string name = slices[1];
            foreach (var kw in KnownCategoryKeywords)
            {
                if (name.Equals(kw, StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith(kw + "-", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith(kw + "_", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith("-" + kw, StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith("_" + kw, StringComparison.OrdinalIgnoreCase))
                {
                    AddTag(kw);
                }
            }
        }

        // 3. Hashtags in the comment (e.g. "#intent #personal")
        if (!string.IsNullOrWhiteSpace(entry.Comment))
        {
            foreach (var w in entry.Comment.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (w.StartsWith('#') && w.Length > 1) AddTag(w.TrimStart('#').ToLowerInvariant());
            }
        }

        return list;
    }

    public static bool HasTag(VaultEntry entry, string tag) =>
        Derive(entry).Contains(tag.Trim().TrimStart('#'), StringComparer.OrdinalIgnoreCase);

    private static bool IsCategoryKeyword(string value) =>
        KnownCategoryKeywords.Any(k => string.Equals(k, value, StringComparison.OrdinalIgnoreCase));
}
