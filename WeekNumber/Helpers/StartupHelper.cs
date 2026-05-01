using Microsoft.Win32;

namespace WeekNumber.Helpers;

public static class StartupHelper
{
    private const string AppName = "WeekNumber_By_Anthony_Tran";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    // StartupApproved binary format: first byte 0x02 = enabled, 0x03 = disabled by user/system.
    private static readonly byte[] ApprovedEnabledValue = [0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    public static void SetStartup(bool enable)
    {
        try
        {
            if (enable)
            {
                var path = GetValidatedQuotedPath();
                if (path == null) return;

                using var runKey = Registry.CurrentUser.CreateSubKey(RunKey, true);
                if (runKey == null) return;
                runKey.SetValue(AppName, path);

                // Mark as approved so Windows doesn't suppress launch via StartupApproved.
                MarkStartupApproved();
            }
            else
            {
                using var runKey = Registry.CurrentUser.OpenSubKey(RunKey, true);
                runKey?.DeleteValue(AppName, false);

                // Also remove the StartupApproved entry to keep state consistent.
                using var approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedKey, true);
                approvedKey?.DeleteValue(AppName, false);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException
                                       or InvalidOperationException or System.Security.SecurityException)
        {
            // Registry operation denied — silently skip.
        }
    }

    public static void UpdateRegistryKey()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key == null) return;

            var registryExePath = key.GetValue(AppName) as string;
            if (string.IsNullOrEmpty(registryExePath)) return;

            var newPath = GetValidatedQuotedPath();
            if (newPath == null) return;

            if (!string.Equals(registryExePath, newPath, StringComparison.OrdinalIgnoreCase))
                key.SetValue(AppName, newPath);

            // Re-approve on every launch to counteract external disabling (Task Manager, Settings, etc.)
            MarkStartupApproved();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException
                                       or InvalidOperationException or System.Security.SecurityException)
        {
            // Registry update failed — leave existing value intact.
        }
    }

    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            if (key?.GetValue(AppName) == null) return false;

            // Check whether Windows has suppressed this entry.
            return !IsDisabledByStartupApproved();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException
                                       or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static bool IsDisabledByStartupApproved()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupApprovedKey, false);
            if (key == null) return false;

            if (key.GetValue(AppName) is not byte[] { Length: > 0 } value)
                return false;

            return value[0] == 0x03;
        }
        catch
        {
            return false;
        }
    }

    private static void MarkStartupApproved()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(StartupApprovedKey, true);
            key?.SetValue(AppName, ApprovedEnabledValue, RegistryValueKind.Binary);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException
                                       or InvalidOperationException or System.Security.SecurityException)
        {
            // Non-critical — app may still launch if not explicitly blocked.
        }
    }

    /// <summary>
    /// Returns the quoted, canonicalised path of the currently running executable after
    /// validating it is safe to persist into the registry Run key.
    /// </summary>
    private static string? GetValidatedQuotedPath()
    {
        // Environment.ProcessPath is the most reliable API for single-file published apps.
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return null;

        // Canonicalize to resolve symlinks, relative segments, etc.
        var canonical = Path.GetFullPath(exePath);

        // Security: reject if the file doesn't actually exist on disk.
        if (!File.Exists(canonical)) return null;

        // Security: must be an .exe to prevent registry value abuse.
        if (!canonical.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;

        // Security: reject control characters and shell metacharacters that could
        // enable injection if the value is ever interpreted by a shell or CreateProcess.
        if (ContainsDangerousCharacters(canonical)) return null;

        // Security: reject unreasonably long paths (MAX_PATH).
        if (canonical.Length > 260) return null;

        // Always quote the path. This prevents argument injection via spaces and is
        // safe even for paths without spaces.
        return $"\"{canonical}\"";
    }

    private static bool ContainsDangerousCharacters(string path)
    {
        foreach (var c in path)
        {
            if (c < 0x20) return true;   // Control characters
            if (c is '%' or '|' or '>' or '<' or '&' or '^' or '!' or '`' or '$' or '{' or '}') return true;
        }
        return false;
    }
}
