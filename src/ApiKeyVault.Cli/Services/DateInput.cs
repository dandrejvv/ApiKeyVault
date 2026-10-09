using System.Globalization;
using System.Text.RegularExpressions;

namespace ApiKeyVault.Cli.Services;

/// <summary>
/// Parses --expires / --review-by values. Accepts YYYY-MM-DD, a full ISO 8601 timestamp,
/// a relative offset (+30d, +12w, +6m, +1y) or "none" to clear the date.
/// A bare date means the end of that day in local time, matching the desktop UI.
/// </summary>
public static partial class DateInput
{
    public const string Help = "YYYY-MM-DD, +30d / +12w / +6m / +1y, or none";

    /// <param name="value">The parsed date, or null when the input asks to clear the date.</param>
    public static bool TryParse(string input, DateTimeOffset now, out DateTimeOffset? value)
    {
        value = null;
        string text = input.Trim();

        if (text.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("never", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var relative = RelativePattern().Match(text);
        if (relative.Success)
        {
            int amount = int.Parse(relative.Groups[1].Value, CultureInfo.InvariantCulture);
            var today = now.ToLocalTime().Date;
            DateTime target = char.ToLowerInvariant(relative.Groups[2].Value[0]) switch
            {
                'd' => today.AddDays(amount),
                'w' => today.AddDays(amount * 7),
                'm' => today.AddMonths(amount),
                _ => today.AddYears(amount)
            };
            value = EndOfDay(target);
            return true;
        }

        if (DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            value = EndOfDay(date);
            return true;
        }

        if (text.Contains('T') &&
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var timestamp))
        {
            value = timestamp;
            return true;
        }

        return false;
    }

    private static DateTimeOffset EndOfDay(DateTime date)
    {
        var local = date.Date.AddDays(1).AddSeconds(-1);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    [GeneratedRegex(@"^\+(\d{1,4})\s*([dwmy])$", RegexOptions.IgnoreCase)]
    private static partial Regex RelativePattern();
}
