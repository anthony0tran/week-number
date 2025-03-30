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
    private bool _disposed;

    public static NotificationAreaIcon Instance => _instance.Value;

    private NotificationAreaIcon()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Information,
            Text = "Week Number",
            Visible = true
        };

        _notifyIcon.MouseClick += NotifyIcon_MouseClick;
    }

    private void NotifyIcon_MouseClick(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }
        
        var weekNumber = ISOWeek.GetWeekOfYear(DateTime.Now);
        _notifyIcon.ShowBalloonTip(3000, "Week Number", $"Current week: {weekNumber}", ToolTipIcon.Info);
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
