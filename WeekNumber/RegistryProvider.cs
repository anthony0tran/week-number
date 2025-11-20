using Microsoft.Win32;

namespace WeekNumber;

public class RegistryProvider : IRegistryProvider
{
    public RegistryKey OpenSubKey(string name, bool writable = false)
    {
        return Registry.CurrentUser.OpenSubKey(name, writable);
    }
}