using System.Runtime.InteropServices;

namespace WeekNumber.Forms;

internal static class DwmInterop
{
    // Applies Windows rounded corner preference.
    public static void SetRoundedCorners(IntPtr hwnd)
    {
        const int dwmwaWindowCornerPreference = 33;
        const int dwmwcpRound = 2;
        
        var preferRound = dwmwcpRound;
        DwmSetWindowAttribute(hwnd, dwmwaWindowCornerPreference, ref preferRound, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
}