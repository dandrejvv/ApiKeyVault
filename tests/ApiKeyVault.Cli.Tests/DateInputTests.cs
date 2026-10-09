using ApiKeyVault.Cli.Services;
using Xunit;

namespace ApiKeyVault.Cli.Tests;

public class DateInputTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("+30d", 30)]
    [InlineData("+2w", 14)]
    [InlineData("+1D", 1)]
    public void RelativeDays_ResolveToEndOfDay(string input, int days)
    {
        Assert.True(DateInput.TryParse(input, Now, out var value));
        var expectedDay = Now.ToLocalTime().Date.AddDays(days);
        Assert.Equal(expectedDay, value!.Value.Date);
        Assert.Equal(new TimeSpan(23, 59, 59), value.Value.TimeOfDay);
    }

    [Fact]
    public void RelativeMonthsAndYears_UseCalendarArithmetic()
    {
        Assert.True(DateInput.TryParse("+6m", Now, out var months));
        Assert.Equal(Now.ToLocalTime().Date.AddMonths(6), months!.Value.Date);

        Assert.True(DateInput.TryParse("+1y", Now, out var years));
        Assert.Equal(Now.ToLocalTime().Date.AddYears(1), years!.Value.Date);
    }

    [Fact]
    public void IsoDate_IsEndOfThatDay()
    {
        Assert.True(DateInput.TryParse("2026-12-31", Now, out var value));
        Assert.Equal(new DateTime(2026, 12, 31, 23, 59, 59), value!.Value.DateTime);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("Never")]
    public void None_ClearsTheDate(string input)
    {
        Assert.True(DateInput.TryParse(input, Now, out var value));
        Assert.Null(value);
    }

    [Theory]
    [InlineData("31/02/2026")]
    [InlineData("2026-02-31")]
    [InlineData("tomorrow")]
    [InlineData("+30")]
    [InlineData("")]
    public void Invalid_IsRejected(string input)
    {
        Assert.False(DateInput.TryParse(input, Now, out _));
    }
}
