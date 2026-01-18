using System.Drawing.Drawing2D;

namespace WeekNumber.Forms;

internal static class GraphicsUtil
{
    // Creates a rounded rectangle path for borders/regions.
    public static GraphicsPath CreateRoundedRect(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, d, d, 180f, 90f);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270f, 90f);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0f, 90f);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90f, 90f);
        path.CloseFigure();
        return path;
    }
}
