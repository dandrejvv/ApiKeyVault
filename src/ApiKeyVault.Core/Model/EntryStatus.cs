namespace ApiKeyVault.Core.Model;

public enum KeyStatus
{
    Ok,
    Due,
    Expired,
    Failing,
    Revoked,
    Compromised
}

public static class EntryStatusCalculator
{
    public static KeyStatus Compute(VaultEntry entry, DateTimeOffset now, int dueWindowDays = 30)
    {
        if (entry.IsCompromised)
        {
            return KeyStatus.Compromised;
        }

        if (entry.IsRevoked)
        {
            return KeyStatus.Revoked;
        }

        if (IsFailedTest(entry.LastTest))
        {
            return KeyStatus.Failing;
        }

        if (entry.Expires.HasValue && entry.Expires.Value <= now)
        {
            return KeyStatus.Expired;
        }

        var threshold = now.AddDays(dueWindowDays);

        if (entry.Expires.HasValue && entry.Expires.Value <= threshold)
        {
            return KeyStatus.Due;
        }

        if (entry.ReviewBy.HasValue && entry.ReviewBy.Value <= threshold)
        {
            return KeyStatus.Due;
        }

        return KeyStatus.Ok;
    }

    /// <summary>Everything except healthy and deliberately revoked keys needs attention.</summary>
    public static bool NeedsAttention(KeyStatus status) => status is not (KeyStatus.Ok or KeyStatus.Revoked);

    /// <summary>
    /// A test counts as failed only when the provider answered with an HTTP error.
    /// Older vaults may hold "could not test" results (no status code), which are ignored.
    /// </summary>
    public static bool IsFailedTest(EntryTestResult? test) =>
        test is { Success: false, StatusCode: not null };
}
