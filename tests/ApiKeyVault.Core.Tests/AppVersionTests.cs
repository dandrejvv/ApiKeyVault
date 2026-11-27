using ApiKeyVault.Core;

namespace ApiKeyVault.Core.Tests;

public class AppVersionTests
{
    [Fact]
    public void GetVersion_StripsBuildMetadataSuffix()
    {
        string version = AppVersion.GetVersion(typeof(AppVersion).Assembly);
        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.DoesNotContain('+', version);
        Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", version);
    }

    [Fact]
    public void GetDisplayVersion_PrefixesWithV()
    {
        string version = AppVersion.GetVersion(typeof(AppVersion).Assembly);
        string display = AppVersion.GetDisplayVersion(typeof(AppVersion).Assembly);
        Assert.Equal($"v{version}", display);
    }
}
