namespace WeekNumber;

public static class StartupManager
{
    private const string AppName = "WeekNumber";
    private const string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void SetStartup(bool enable)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryKey, true);
        if (key == null) return;

        if (enable)
        {
            var exePath = Application.ExecutablePath;
            key.SetValue(AppName, exePath);
        }
        else
        {
            key.DeleteValue(AppName, false);
        }
    }

    public static bool IsStartupEnabled()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryKey);
        return key?.GetValue(AppName) != null;
    }
}