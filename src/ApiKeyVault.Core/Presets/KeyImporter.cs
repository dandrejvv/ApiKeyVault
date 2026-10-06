using System.Text.Json;
using System.Text.RegularExpressions;

namespace ApiKeyVault.Core.Presets;

public sealed record ImportedKeyCandidate(
    string RawKeyName,
    string Secret,
    string SuggestedProvider,
    string SuggestedName
);

public static class KeyImporter
{
    public static List<ImportedKeyCandidate> ParseContent(string content)
    {
        var results = new List<ImportedKeyCandidate>();
        if (string.IsNullOrWhiteSpace(content)) return results;

        string trimmed = content.Trim();

        // 1. Try JSON
        if ((trimmed.StartsWith('{') && trimmed.EndsWith('}')) ||
            (trimmed.StartsWith('[') && trimmed.EndsWith(']')))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.String)
                        {
                            string keyName = prop.Name;
                            string secret = prop.Value.GetString() ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(secret))
                            {
                                results.Add(CreateCandidate(keyName, secret));
                            }
                        }
                    }
                    if (results.Count > 0) return results;
                }
            }
            catch
            {
                // Not valid JSON, continue with line-by-line parsing
            }
        }

        // 2. Line by line parsing (.env or loose lines)
        using var reader = new StringReader(content);
        string? line;
        int lineNum = 0;
        while ((line = reader.ReadLine()) != null)
        {
            lineNum++;
            string trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith('#'))
            {
                continue;
            }

            int eqIndex = trimmedLine.IndexOf('=');
            if (eqIndex > 0)
            {
                string keyName = trimmedLine[..eqIndex].Trim();
                string secret = trimmedLine[(eqIndex + 1)..].Trim();

                // Strip quotes if wrapped
                if ((secret.StartsWith('"') && secret.EndsWith('"')) ||
                    (secret.StartsWith('\'') && secret.EndsWith('\'')))
                {
                    secret = secret[1..^1];
                }

                if (!string.IsNullOrWhiteSpace(secret))
                {
                    results.Add(CreateCandidate(keyName, secret));
                }
            }
            else
            {
                // Loose line
                results.Add(CreateCandidate($"KEY_{lineNum}", trimmedLine));
            }
        }

        return results;
    }

    private static ImportedKeyCandidate CreateCandidate(string rawName, string secret)
    {
        var preset = PresetRegistry.GuessFromKey(secret);
        string provider = preset?.Id ?? GuessProviderFromName(rawName) ?? "other";
        string name = SuggestKeyName(rawName, provider);

        return new ImportedKeyCandidate(rawName, secret, provider, name);
    }

    private static string? GuessProviderFromName(string rawName)
    {
        string upper = rawName.ToUpperInvariant();
        if (upper.Contains("OPENAI")) return "openai";
        if (upper.Contains("ANTHROPIC") || upper.Contains("CLAUDE")) return "anthropic";
        if (upper.Contains("OPENROUTER")) return "openrouter";
        if (upper.Contains("AZURE")) return "azure-openai";
        if (upper.Contains("GEMINI")) return "gemini";
        if (upper.Contains("CLOCKIFY")) return "clockify";
        if (upper.Contains("SERPER")) return "serper";
        return null;
    }

    private static string SuggestKeyName(string rawName, string provider)
    {
        string clean = rawName.ToLowerInvariant();
        clean = Regex.Replace(clean, @"^(export\s+)?", "");

        var parts = clean.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var filtered = parts.Where(p =>
            p != provider &&
            p != "api" &&
            p != "key" &&
            p != "keys" &&
            p != "token" &&
            p != "secret" &&
            p != "auth").ToList();

        if (filtered.Count == 0)
        {
            return "default";
        }

        string result = string.Join("-", filtered);
        result = Regex.Replace(result, @"[^a-z0-9_-]", "-");
        result = Regex.Replace(result, @"-+", "-").Trim('-');
        return string.IsNullOrEmpty(result) ? "default" : result;
    }
}
