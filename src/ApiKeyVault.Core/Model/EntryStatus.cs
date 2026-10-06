namespace ApiKeyVault.Core.Model;

public enum KeyStatus
{
    Ok,
    Due,
    Expired,
    Failing
}

public static class EntryStatusCalculator
{
    public static KeyStatus Compute(VaultEntry entry, DateTimeOffset now, int dueWindowDays = 30)
    {
        if (entry.LastTest != null && !entry.LastTest.Success)
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
}
