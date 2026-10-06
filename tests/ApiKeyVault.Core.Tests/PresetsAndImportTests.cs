using ApiKeyVault.Core.Presets;

namespace ApiKeyVault.Core.Tests;

public class PresetsAndImportTests
{
    [Fact]
    public void LongestPrefixMatching_AnthropicOverOpenAi()
    {
        string anthropicKey = "sk-ant-api03-abcdef123456";
        var preset = PresetRegistry.GuessFromKey(anthropicKey);

        Assert.NotNull(preset);
        Assert.Equal("anthropic", preset.Id);
    }

    [Fact]
    public void LongestPrefixMatching_OpenRouterOverOpenAi()
    {
        string openRouterKey = "sk-or-v1-abcdef123456";
        var preset = PresetRegistry.GuessFromKey(openRouterKey);

        Assert.NotNull(preset);
        Assert.Equal("openrouter", preset.Id);
    }

    [Fact]
    public void LongestPrefixMatching_OpenAi()
    {
        string openAiKey = "sk-proj-abcdef123456";
        var preset = PresetRegistry.GuessFromKey(openAiKey);

        Assert.NotNull(preset);
        Assert.Equal("openai", preset.Id);
    }

    [Fact]
    public void KeyImporter_ParsesEnvFormat()
    {
        string envContent = @"
# Comments should be ignored
OPENAI_API_KEY_PERSONAL=sk-proj-1234567890
ANTHROPIC_KEY_WORK=""sk-ant-api03-9876543210""
";

        var candidates = KeyImporter.ParseContent(envContent);

        Assert.Equal(2, candidates.Count);

        var c1 = candidates[0];
        Assert.Equal("openai", c1.SuggestedProvider);
        Assert.Equal("personal", c1.SuggestedName);
        Assert.Equal("sk-proj-1234567890", c1.Secret);

        var c2 = candidates[1];
        Assert.Equal("anthropic", c2.SuggestedProvider);
        Assert.Equal("work", c2.SuggestedName);
        Assert.Equal("sk-ant-api03-9876543210", c2.Secret);
    }

    [Fact]
    public void KeyImporter_ParsesJsonFormat()
    {
        string json = @"
{
    ""OPENAI_KEY"": ""sk-proj-111111"",
    ""GEMINI_KEY"": ""AIzaSyD-sample-gemini-key-12345678""
}
";
        var candidates = KeyImporter.ParseContent(json);

        Assert.Equal(2, candidates.Count);
        Assert.Contains(candidates, c => c.SuggestedProvider == "openai" && c.Secret == "sk-proj-111111");
        Assert.Contains(candidates, c => c.SuggestedProvider == "gemini" && c.Secret.StartsWith("AIza"));
    }
}
