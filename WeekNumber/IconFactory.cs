using System.Drawing.Text;

namespace WeekNumber;

public class IconFactory : IIconFactory
{
    public Icon CreateNumberIcon(int number, Font font, Brush brush, int iconSize)
    {
        using var bitmap = new Bitmap(iconSize, iconSize);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);

        var text = number.ToString();
        const int margin = -32;
        using var adjustedFont = new Font(font.FontFamily, iconSize - 1, font.Style, GraphicsUnit.Pixel);
        var size = graphics.MeasureString(text, adjustedFont);

        var x = Math.Max((iconSize - size.Width) / 2, margin);
        var y = Math.Max((iconSize - size.Height) / 2, margin);

        graphics.DrawString(text, adjustedFont, brush, x, y);

        var handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }
}