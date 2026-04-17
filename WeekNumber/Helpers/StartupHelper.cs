namespace WeekNumber.Helpers;

public static class StartupHelper
{
    private const string AppName = "WeekNumber_By_Anthony_Tran";
    private const string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void SetStartup(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegistryKey, true);
            if (key == null) return;
            if (!enable)
            {
                key.DeleteValue(AppName, false);
                return;
            }
            var path = GetTrustedQuotedPath();
            if (path == null) return;
            key.SetValue(AppName, path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            // Registry write denied — silently skip; startup won't be persisted.
        }
    }

    public static void UpdateRegistryKey()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryKey, true);
            if (key == null) return;
            var registryExePath = key.GetValue(AppName) as string;
            if (string.IsNullOrEmpty(registryExePath)) return;
            var newPath = GetTrustedQuotedPath();
            if (newPath == null) return;
            if (string.Equals(registryExePath, newPath, StringComparison.OrdinalIgnoreCase)) return;
            key.SetValue(AppName, newPath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            // Registry update failed — leave existing value intact.
        }
    }

    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryKey, false);
            return key?.GetValue(AppName) != null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    // Returns the quoted, canonicalised executable path only when it lives under a
    // trusted directory (Program Files or the app's own base directory). Returns null
    // if the path cannot be trusted, preventing a malicious side-loaded copy from
    // persisting itself to the Run key.
    private static string? GetTrustedQuotedPath()
    {
        var exePath = Application.ExecutablePath;
        if (string.IsNullOrEmpty(exePath)) return null;

        var canonical = Path.GetFullPath(exePath);

        var trusted = IsTrustedLocation(canonical);
        if (!trusted) return null;

        // Quote path so spaces don't cause mis-parsing if the value is ever read by
        // a legacy CreateProcess caller.
        return canonical.Contains(' ') ? $"\"{canonical}\"" : canonical;
    }

    private static bool IsTrustedLocation(string path)
    {
        string[] trustedRoots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            AppContext.BaseDirectory
        ];

        return trustedRoots
            .Where(r => !string.IsNullOrEmpty(r))
            .Select(r => Path.GetFullPath(r))
            .Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase));
    }
}
