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
    private readonly WeekNumber _weekNumber = new();

    private readonly Font _font = new("Segoe UI", 40, FontStyle.Regular);

    public static NotificationAreaIcon Instance => _instance.Value;

    private NotificationAreaIcon()
    {
        var exitMenuItem = new ToolStripMenuItem("Exit", null, MenuExit_Click);
        var startupMenuItem = new ToolStripMenuItem("Run at Startup", null, MenuStartup_Click)
        {
            Checked = StartupManager.IsStartupEnabled()
        };
        
        _contextMenu.Items.Add(startupMenuItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(exitMenuItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = CreateNumberIcon(_weekNumber.Number),
            Text = $"Last updated on: {_weekNumber.LastUpdated.ToString("g", new CultureInfo("nl-NL"))}",
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

        _weekNumber.UpdateNumber();

        UpdateIcon();
        UpdateText();
        _notifyIcon.ShowBalloonTip(2000, $"Week: {_weekNumber.Number}",
            $"{_weekNumber.LastUpdated.ToString("g", new CultureInfo("nl-NL"))}", ToolTipIcon.Info);
    }
    
    private void MenuStartup_Click(object? sender, EventArgs e)
    {
        var menuItem = (ToolStripMenuItem)sender!;
        menuItem.Checked = !menuItem.Checked;
        StartupManager.SetStartup(menuItem.Checked);
    }

    private static void MenuExit_Click(object? sender, EventArgs e)
    {
        Application.Exit();
    }

    private void UpdateIcon()
    {
        _notifyIcon.Icon = CreateNumberIcon(_weekNumber.Number);
    }

    private void UpdateText()
    {
        _notifyIcon.Text = $"Last updated on: {_weekNumber.LastUpdated.ToString("g", new CultureInfo("nl-NL"))}";
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