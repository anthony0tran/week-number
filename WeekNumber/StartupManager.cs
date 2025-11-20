namespace WeekNumber;

public class StartupManager(IRegistryProvider registryProvider, IExecutablePathProvider exePathProvider)
{
    private const string AppName = "WeekNumber_By_Anthony_Tran";
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public void SetStartup(bool enable)
    {
        using var key = registryProvider.OpenSubKey(RegistryKeyPath, true);

        if (!enable)
        {
            key.DeleteValue(AppName, false);
            return;
        }

        var exePath = exePathProvider.GetExecutablePath();
        key.SetValue(AppName, exePath);
    }

    public void UpdateRegistryKey()
    {
        using var key = registryProvider.OpenSubKey(RegistryKeyPath, true);

        var registryExePath = key.GetValue(AppName) as string;
        if (string.IsNullOrEmpty(registryExePath)) return;

        var exePath = exePathProvider.GetExecutablePath();
        if (string.Equals(registryExePath, exePath, StringComparison.OrdinalIgnoreCase)) return;

        key.SetValue(AppName, exePath);
    }

    public bool IsStartupEnabled()
    {
        using var key = registryProvider.OpenSubKey(RegistryKeyPath);
        return key.GetValue(AppName) != null;
    }
}