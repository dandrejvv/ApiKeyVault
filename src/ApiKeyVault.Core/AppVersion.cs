using System.Reflection;

namespace ApiKeyVault.Core;

/// <summary>
/// Reads the build version from assembly metadata, stripping any +gitsha build suffix.
/// </summary>
public static class AppVersion
{
    public static string GetVersion(Assembly assembly)
    {
        var raw = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                  ?? assembly.GetName().Version?.ToString(3)
                  ?? "0.0.0";

        int plusIndex = raw.IndexOf('+');
        return plusIndex >= 0 ? raw[..plusIndex] : raw;
    }

    public static string GetDisplayVersion(Assembly assembly)
    {
        string version = GetVersion(assembly);
        return version.StartsWith('v') ? version : $"v{version}";
    }
}
