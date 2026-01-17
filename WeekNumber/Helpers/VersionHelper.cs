using System.Reflection;

namespace WeekNumber.Helpers;

public static class VersionHelper
{
    public static string GetAppVersion()
    {
        // Prefer AssemblyFileVersionAttribute if present.
        var fileVer = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyFileVersionAttribute>()
            ?.Version;

        if (!string.IsNullOrWhiteSpace(fileVer))
        {
            var parts = fileVer.Split('.');
            if (parts.Length >= 3)
                return string.Join('.', parts[0], parts[1], parts[2]);
            return fileVer;
        }

        // Fallback to AssemblyInformationalVersionAttribute (strip any +commit metadata).
        var infoVer = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(infoVer))
        {
            var plusIndex = infoVer.IndexOf('+');
            var ver = plusIndex >= 0 ? infoVer.Substring(0, plusIndex) : infoVer;
            var parts = ver.Split('.');
            if (parts.Length >= 3)
                return string.Join('.', parts[0], parts[1], parts[2]);
            return ver;
        }

        // Final fallback to Application.ProductVersion.
        var prodVerParts = Application.ProductVersion.Split('.');
        if (prodVerParts.Length >= 3)
            return string.Join('.', prodVerParts[0], prodVerParts[1], prodVerParts[2]);
        return Application.ProductVersion;
    }
}