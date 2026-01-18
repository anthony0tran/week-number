using System.Runtime.InteropServices;

namespace WeekNumber.Forms;

internal static class DwmInterop
{
    // Applies Windows rounded corner preference.
    public static void SetRoundedCorners(IntPtr hwnd)
    {
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWCP_ROUND = 2;
        try
        {
            int preferRound = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preferRound, sizeof(int));
        }
        catch
        {
            // No-op on platforms without DWM.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
}
