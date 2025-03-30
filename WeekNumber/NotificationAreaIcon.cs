using System.Drawing.Text;
using System.Globalization;

namespace WeekNumber;

/// <summary>
/// Manages a singleton instance of a Windows notification area icon.
/// Provides functionality to display the current ISO week number
/// and update the icon during runtime.
/// </summary>
/// <remarks>
/// This class follows the singleton pattern to ensure only one notification
/// area icon exists throughout the application's lifetime. It implements
/// IDisposable for proper cleanup of system resources.
/// </remarks>
public sealed class NotificationAreaIcon : IDisposable
{
    // ReSharper disable once InconsistentNaming
    private static readonly Lazy<NotificationAreaIcon> _instance = new(() => new NotificationAreaIcon());
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu = new();
    private bool _disposed;
    
    private readonly Font _font = new("Segoe UI", 48, FontStyle.Regular);

    public static NotificationAreaIcon Instance => _instance.Value;

    private NotificationAreaIcon()
    {
        var exitMenuItem = new ToolStripMenuItem("Exit", null, MenuExit_Click);
        _contextMenu.Items.Add(exitMenuItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = CreateNumberIcon(ISOWeek.GetWeekOfYear(DateTime.Now)),
            Text = "Week Number",
            Visible = true,
            ContextMenuStrip = _contextMenu
        };

        _notifyIcon.MouseClick += NotifyIcon_LeftMouseClick;
    }

    private void NotifyIcon_LeftMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        var weekNumber = ISOWeek.GetWeekOfYear(DateTime.Now);
        _notifyIcon.ShowBalloonTip(3000, "Week Number", $"Current week: {weekNumber}", ToolTipIcon.Info);
    }

    private static void MenuExit_Click(object? sender, EventArgs e)
    {
        Application.Exit();
    }

    public void UpdateIcon(Icon newIcon)
    {
        _notifyIcon.Icon = newIcon;
    }
    
    private Icon CreateNumberIcon(int number)
    {
        using var bitmap = new Bitmap(48, 48);
        using var graphics = Graphics.FromImage(bitmap);
        
        // Set up high quality rendering
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        
        // Clear background to transparent
        graphics.Clear(Color.Transparent);
        
        // Draw the number
        var text = number.ToString();
        var size = graphics.MeasureString(text, _font);
        var x = (48 - size.Width) / 2;
        var y = (48 - size.Height) / 2;
        
        graphics.DrawString(text, _font, Brushes.White, x, y);
        
        // Convert to icon
        var handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.Dispose();
        _disposed = true;
    }
}