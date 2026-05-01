using System.Runtime.InteropServices;

namespace WeekNumber.Forms;

internal static class DwmInterop
{
    // Applies Windows rounded corner preference.
    public static void SetRoundedCorners(IntPtr hwnd)
    {
        // Validate handle to prevent P/Invoke with an invalid or attacker-controlled HWND.
        if (hwnd == IntPtr.Zero)
            return;

        const int dwmwaWindowCornerPreference = 33;
        const int dwmwcpRound = 2;
        
        var preferRound = dwmwcpRound;
        _ = DwmSetWindowAttribute(hwnd, dwmwaWindowCornerPreference, ref preferRound, sizeof(int));
    }

    [DllImport("dwmapi.dll", ExactSpelling = true, SetLastError = false)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
}