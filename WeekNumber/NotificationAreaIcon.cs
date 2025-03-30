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
    private static readonly Lazy<NotificationAreaIcon> _instance = new(() => new NotificationAreaIcon());
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private bool _disposed;

    public static NotificationAreaIcon Instance => _instance.Value;

    private NotificationAreaIcon()
    {
        _contextMenu = new ContextMenuStrip();
        var exitMenuItem = new ToolStripMenuItem("Exit", null, MenuExit_Click);
        _contextMenu.Items.Add(exitMenuItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Information,
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