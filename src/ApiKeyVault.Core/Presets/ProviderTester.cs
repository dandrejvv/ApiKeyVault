using System.Diagnostics;

namespace ApiKeyVault.Core.Presets;

public sealed record TestResult(
    bool Success,
    bool CouldNotTest,
    int? StatusCode,
    string Message,
    long ElapsedMilliseconds
);

public interface IProviderTester
{
    Task<TestResult> TestKeyAsync(
        string provider,
        string secret,
        IReadOnlyDictionary<string, string>? extraFields = null,
        CancellationToken cancellationToken = default);
}

public sealed class HttpProviderTester : IProviderTester
{
    private readonly HttpClient _httpClient;

    public HttpProviderTester(HttpClient? httpClient = null)
    {
        if (httpClient != null)
        {
            _httpClient = httpClient;
        }
        else
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false // Strict requirement: redirects are not followed!
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
        }
    }

    private static readonly HashSet<string> SupportedProvidersSet = new(StringComparer.OrdinalIgnoreCase)
    {
        "openai",
        "anthropic",
        "openrouter",
        "azure-openai",
        "gemini",
        "clockify",
        "github",
        "gitlab"
    };

    public static bool IsSupported(string provider) =>
        !string.IsNullOrWhiteSpace(provider) && SupportedProvidersSet.Contains(provider.Trim());

    public static IReadOnlyList<string> SupportedProviders => SupportedProvidersSet.ToList();

    public async Task<TestResult> TestKeyAsync(
        string provider,
        string secret,
        IReadOnlyDictionary<string, string>? extraFields = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        string normProvider = provider.Trim().ToLowerInvariant();

        try
        {
            HttpRequestMessage request;
            switch (normProvider)
            {
                case "openai":
                    request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
                    request.Headers.Add("Authorization", $"Bearer {secret}");
                    if (extraFields?.TryGetValue("organization", out var org) == true && !string.IsNullOrWhiteSpace(org))
                    {
                        request.Headers.Add("OpenAI-Organization", org);
                    }
                    break;

                case "anthropic":
                    request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models");
                    request.Headers.Add("x-api-key", secret);
                    request.Headers.Add("anthropic-version", "2023-06-01");
                    break;

                case "openrouter":
                    request = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/key");
                    request.Headers.Add("Authorization", $"Bearer {secret}");
                    break;

                case "azure-openai":
                    string? endpoint = extraFields?.GetValueOrDefault("endpoint");
                    if (string.IsNullOrWhiteSpace(endpoint))
                    {
                        return new TestResult(false, true, null, "Azure OpenAI requires endpoint extra field.", sw.ElapsedMilliseconds);
                    }
                    if (!endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        return new TestResult(false, true, null, "Endpoint must use https://", sw.ElapsedMilliseconds);
                    }
                    string apiVersion = extraFields?.GetValueOrDefault("api_version") ?? "2024-02-01";
                    string uri = $"{endpoint.TrimEnd('/')}/openai/models?api-version={apiVersion}";
                    request = new HttpRequestMessage(HttpMethod.Get, uri);
                    request.Headers.Add("api-key", secret);
                    break;

                case "gemini":
                    request = new HttpRequestMessage(HttpMethod.Get, "https://generativelanguage.googleapis.com/v1beta/models");
                    request.Headers.Add("x-goog-api-key", secret);
                    break;

                case "clockify":
                    request = new HttpRequestMessage(HttpMethod.Get, "https://api.clockify.me/api/v1/user");
                    request.Headers.Add("X-Api-Key", secret);
                    break;

                case "github":
                    request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
                    request.Headers.Add("Authorization", $"Bearer {secret}");
                    request.Headers.Add("User-Agent", "ApiKeyVault");
                    break;

                case "gitlab":
                    request = new HttpRequestMessage(HttpMethod.Get, "https://gitlab.com/api/v4/user");
                    request.Headers.Add("PRIVATE-TOKEN", secret);
                    break;

                default:
                    return new TestResult(false, true, null, $"No automated test available for '{provider}'. Supported: OpenAI, Anthropic, Gemini, OpenRouter, Azure OpenAI, Clockify, GitHub, GitLab.", sw.ElapsedMilliseconds);
            }

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            sw.Stop();

            int code = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                return new TestResult(true, false, code, $"Valid ({sw.ElapsedMilliseconds} ms)", sw.ElapsedMilliseconds);
            }
            if (code >= 300 && code < 400)
            {
                return new TestResult(false, true, code, $"Redirect {code} (redirects not followed)", sw.ElapsedMilliseconds);
            }
            if (code == 401 || code == 403)
            {
                return new TestResult(false, false, code, $"{code} invalid or revoked", sw.ElapsedMilliseconds);
            }

            return new TestResult(false, false, code, $"HTTP {code} error", sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult(false, true, null, $"Could not test: {ex.Message}", sw.ElapsedMilliseconds);
        }
    }
}
