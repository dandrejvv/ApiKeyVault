using ApiKeyVault.Core.Cryptography;
using ApiKeyVault.Core.Model;
using ApiKeyVault.Core.Presets;
using ApiKeyVault.Core.Storage;
using ApiKeyVault.Core.Vault;

namespace ApiKeyVault.Core.Tests;

public class TestResultRecordingTests : IDisposable
{
    private readonly string _tempDir;
    private readonly VaultSession _session;

    private static readonly Argon2idParameters FastKdf = new()
    {
        MemoryKiB = CryptoConstants.MinMemoryKiB,
        Iterations = CryptoConstants.MinIterations,
        Parallelism = 1
    };

    public TestResultRecordingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "akv_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _session = VaultManager.CreateVault(
            Path.Combine(_tempDir, "vault.akv"), "test-pass", "TEST-DEV", FastKdf,
            new InMemoryDeviceKeyStore(), new LocalStateManager(Path.Combine(_tempDir, "state.json"))).Session;
    }

    [Fact]
    public async Task UnsupportedProvider_CouldNotTest_IsNotRecorded()
    {
        var entry = _session.AddEntry("aws", "s3-access", "demo-aws-secret");

        var result = await new HttpProviderTester().TestKeyAsync(entry.Provider, entry.Secret);
        Assert.True(result.CouldNotTest);

        _session.RecordTestResult(entry, result);

        Assert.Null(entry.LastTest);
        Assert.Equal(KeyStatus.Ok, EntryStatusCalculator.Compute(entry, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void HttpRejection_IsRecorded_AndMarksFailing()
    {
        var entry = _session.AddEntry("openai", "prod", "sk-demo");

        _session.RecordTestResult(entry, new TestResult(false, false, 401, "401 invalid or revoked", 50));

        Assert.NotNull(entry.LastTest);
        Assert.Equal(KeyStatus.Failing, EntryStatusCalculator.Compute(entry, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void LegacyCouldNotTestRecord_WithoutStatusCode_DoesNotMarkFailing()
    {
        var entry = _session.AddEntry("stripe", "live-api", "demo-stripe-secret");
        entry.LastTest = new EntryTestResult
        {
            Time = DateTimeOffset.UtcNow,
            Success = false,
            StatusCode = null,
            Message = "No automated test available for 'stripe'."
        };

        Assert.False(EntryStatusCalculator.IsFailedTest(entry.LastTest));
        Assert.Equal(KeyStatus.Ok, EntryStatusCalculator.Compute(entry, DateTimeOffset.UtcNow));
    }

    public void Dispose()
    {
        _session.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
