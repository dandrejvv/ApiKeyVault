namespace ApiKeyVault.Core.Model;

/// <summary>
/// Produces safe display masks for secrets without revealing short keys.
/// </summary>
public static class SecretMasker
{
    public const int EndChars = 2;
    public const int MinHiddenMiddleChars = 8;
    public const int MinLengthForEnds = (EndChars * 2) + MinHiddenMiddleChars;
    public const string FullMask = "••••••••••••••••";
    private const string MiddleMask = "••••••••";

    /// <summary>
    /// Shows the first 2 and last 2 characters separated by a fixed mask when the secret
    /// has at least 8 characters in between (length >= 12); otherwise returns <see cref="FullMask"/>.
    /// </summary>
    public static string MaskWithEnds(string? secret)
    {
        if (string.IsNullOrEmpty(secret) || secret.Length < MinLengthForEnds)
        {
            return FullMask;
        }

        return string.Concat(secret.AsSpan(0, EndChars), MiddleMask, secret.AsSpan(secret.Length - EndChars));
    }
}
