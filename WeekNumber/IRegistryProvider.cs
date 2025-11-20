using Microsoft.Win32;

namespace WeekNumber;

public interface IRegistryProvider
{
    RegistryKey OpenSubKey(string name, bool writable = false);
}