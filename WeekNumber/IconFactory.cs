using System.Drawing.Text;

namespace WeekNumber;

public class IconFactory : IIconFactory
{
    public Icon CreateNumberIcon(int number, Font font, Brush brush, int iconSize)
    {
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
        return Icon.FromHandle(handle);
    }
}