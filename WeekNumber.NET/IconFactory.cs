using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace WeekNumber;

public sealed class IconFactory : IIconFactory
{
    // Constrain week numbers to valid ISO range to prevent unexpected rendering behavior.
    private const int MinWeekNumber = 1;
    private const int MaxWeekNumber = 53;
    private const int MaxIconSize = 256;

    public Icon CreateNumberIcon(int number, Font font, Brush brush, int iconSize)
    {
        // Input validation: reject out-of-range values that could indicate corruption or tampering.
        if (number < MinWeekNumber || number > MaxWeekNumber)
            number = Math.Clamp(number, MinWeekNumber, MaxWeekNumber);

        if (iconSize <= 0 || iconSize > MaxIconSize)
            iconSize = 32;

        using var bitmap = new Bitmap(iconSize, iconSize);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);

        var text = number.ToString();
        using var adjustedFont = new Font(font.FontFamily, iconSize - 2, font.Style, GraphicsUnit.Pixel);

        var size = TextRenderer.MeasureText(graphics, text, adjustedFont, new Size(iconSize, iconSize),
            TextFormatFlags.NoPadding);

        var x = (iconSize - size.Width) / 2;
        var y = (iconSize - size.Height) / 2;

        // Compensate for italic right-side overhang not captured by MeasureText
        if (adjustedFont.Italic)
            x -= (int)(size.Height * 0.1f);

        var color = brush is SolidBrush sb ? sb.Color : Color.White;

        TextRenderer.DrawText(graphics, text, adjustedFont, new Point(x, y), color,
            TextFormatFlags.NoPadding);

        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}