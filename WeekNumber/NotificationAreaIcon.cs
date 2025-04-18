using System.Drawing.Text;
using System.Globalization;
using WeekNumber.Helpers;

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
    private readonly WeekNumber _weekNumber = new();
    private bool _disposed;
    private const int IconSizeInPixels = 265;
    private Brush _currentBrush = BrushHelper.GetBrushFromColor(Properties.Settings.Default.SelectedColor);

    private readonly Font _font = new("Segoe UI", IconSizeInPixels, FontStyle.Bold, GraphicsUnit.Pixel);

    public static NotificationAreaIcon Instance => _instance.Value;

    private NotificationAreaIcon()
    {
        var exitMenuItem = new ToolStripMenuItem("Exit", null, MenuExit_Click);
        var startupMenuItem = new ToolStripMenuItem("Run at Startup", null, MenuStartup_Click)
        {
            Checked = StartupManager.IsStartupEnabled()
        };
        var colorPickerMenuItem = new ToolStripMenuItem("Change color", null, (_, _) =>
        {
            using ColorDialog colorDialog = new()
            {
                AllowFullOpen = true,
                AnyColor = true,
                FullOpen = true, // Ensures the dialog opens with the custom colors section visible
                CustomColors = [0xFF0000, 0x00FF00, 0x0000FF, 0xFFFF00, 0xFF00FF, 0x00FFFF] // Example custom colors
            };
            
            if (colorDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }
            
            _currentBrush = BrushHelper.GetBrushFromColor(colorDialog.Color);
            
            Properties.Settings.Default.SelectedColor = colorDialog.Color;
            Properties.Settings.Default.Save();
            
            UpdateIcon();
        });
        
        _contextMenu.Items.Add(startupMenuItem);
        _contextMenu.Items.Add(colorPickerMenuItem);
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
        
        var calendarForm = new CalendarForm();
        calendarForm.ShowAtCursor();
    }
    
    private static void MenuStartup_Click(object? sender, EventArgs e)
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
        using var bitmap = new Bitmap(IconSizeInPixels, IconSizeInPixels);
        using var graphics = Graphics.FromImage(bitmap);

        // Set up high quality rendering
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

        // Clear background to transparent
        graphics.Clear(Color.Transparent);

        // Draw the number
        var text = number.ToString();
        var size = graphics.MeasureString(text, _font);
        var x = (IconSizeInPixels - size.Width) / 2;
        var y = (IconSizeInPixels - size.Height) / 2;

        graphics.DrawString(text, _font, _currentBrush, x, y);

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