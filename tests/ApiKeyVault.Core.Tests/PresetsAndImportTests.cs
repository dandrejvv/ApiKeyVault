using ApiKeyVault.Core.Presets;

namespace ApiKeyVault.Core.Tests;

public class PresetsAndImportTests
{
    [Fact]
    public void LongestPrefixMatching_AnthropicOverOpenAi()
    {
        string anthropicKey = "sk-ant-api03-mock-token-abc";
        var preset = PresetRegistry.GuessFromKey(anthropicKey);

        Assert.NotNull(preset);
        Assert.Equal("anthropic", preset.Id);
    }

    [Fact]
    public void LongestPrefixMatching_OpenRouterOverOpenAi()
    {
        string openRouterKey = "sk-or-v1-mock-token-abc";
        var preset = PresetRegistry.GuessFromKey(openRouterKey);

        Assert.NotNull(preset);
        Assert.Equal("openrouter", preset.Id);
    }

    [Fact]
    public void LongestPrefixMatching_OpenAi()
    {
        string openAiKey = "sk-proj-mock-token-abc";
        var preset = PresetRegistry.GuessFromKey(openAiKey);

        Assert.NotNull(preset);
        Assert.Equal("openai", preset.Id);
    }

    [Fact]
    public void KeyImporter_ParsesEnvFormat()
    {
        string envContent = @"
# Comments should be ignored
OPENAI_API_KEY_PERSONAL=sk-proj-mock-env-token
ANTHROPIC_KEY_WORK=""sk-ant-api03-mock-env-token""
";

        var candidates = KeyImporter.ParseContent(envContent);

        Assert.Equal(2, candidates.Count);

        var c1 = candidates[0];
        Assert.Equal("openai", c1.SuggestedProvider);
        Assert.Equal("personal", c1.SuggestedName);
        Assert.Equal("sk-proj-mock-env-token", c1.Secret);

        var c2 = candidates[1];
        Assert.Equal("anthropic", c2.SuggestedProvider);
        Assert.Equal("work", c2.SuggestedName);
        Assert.Equal("sk-ant-api03-mock-env-token", c2.Secret);
    }

    [Fact]
    public void KeyImporter_ParsesJsonFormat()
    {
        string json = @"
{
    ""OPENAI_KEY"": ""sk-proj-mock-json-token"",
    ""GEMINI_KEY"": ""AIza-mock-sample-gemini-key""
}
";
        var candidates = KeyImporter.ParseContent(json);

        Assert.Equal(2, candidates.Count);
        Assert.Contains(candidates, c => c.SuggestedProvider == "openai" && c.Secret == "sk-proj-mock-json-token");
        Assert.Contains(candidates, c => c.SuggestedProvider == "gemini" && c.Secret.StartsWith("AIza"));
    }
}
