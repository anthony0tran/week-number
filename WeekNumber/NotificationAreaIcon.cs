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
    private const int IconSizeInPixels = 530;
    private static FontStyle _currentFontStyle = (FontStyle)Properties.Settings.Default.SelectedFontStyle;
    private Brush _currentBrush = BrushHelper.GetBrushFromColor(Properties.Settings.Default.SelectedColor);
    private const string DefaultFontFamily = "Segoe UI";

    private Font _font = new(DefaultFontFamily, IconSizeInPixels, _currentFontStyle, GraphicsUnit.Pixel);

    public static NotificationAreaIcon Instance => _instance.Value;

    private NotificationAreaIcon()
    {
        var exitMenuItem = new ToolStripMenuItem("Exit", null, MenuExit_Click);
        var startupMenuItem = new ToolStripMenuItem("Run at Startup", null, MenuStartup_Click)
        {
            Checked = StartupManager.IsStartupEnabled()
        };
        var colorPickerMenuItem = new ToolStripMenuItem("Change Color", null, (_, _) =>
        {
            using ColorDialog colorDialog = new();
            colorDialog.AllowFullOpen = true;
            colorDialog.AnyColor = true;
            colorDialog.FullOpen = true;
            colorDialog.CustomColors = [0xFF0000, 0x00FF00, 0x0000FF, 0xFFFF00, 0xFF00FF, 0x00FFFF];

            if (colorDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            _currentBrush = BrushHelper.GetBrushFromColor(colorDialog.Color);

            Properties.Settings.Default.SelectedColor = colorDialog.Color;
            Properties.Settings.Default.Save();

            UpdateIcon();
        });

        var fontStyleMenuItem = new ToolStripMenuItem("Font Style");
        foreach (var style in Enum.GetValues<FontStyle>()
                     .Where(s => s is not (FontStyle.Regular or FontStyle.Underline)))
        {
            var styleItem = new ToolStripMenuItem(style.ToString())
            {
                CheckOnClick = true,
                Checked = _currentFontStyle.HasFlag(style) // Restore checked state
            };

            styleItem.CheckedChanged += (_, _) =>
            {
                if (styleItem.Checked)
                {
                    _currentFontStyle |= style; // Add style
                }
                else
                {
                    _currentFontStyle &= ~style; // Remove style
                }

                // Save the updated FontStyle to settings
                Properties.Settings.Default.SelectedFontStyle = (int)_currentFontStyle;
                Properties.Settings.Default.Save();

                _font = new Font(DefaultFontFamily, IconSizeInPixels, _currentFontStyle, GraphicsUnit.Pixel);
                UpdateIcon();
            };

            fontStyleMenuItem.DropDownItems.Add(styleItem);
        }

        _contextMenu.Items.Add(startupMenuItem);
        _contextMenu.Items.Add(colorPickerMenuItem);
        _contextMenu.Items.Add(fontStyleMenuItem);
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
        const int scaleFactor = 4; // Increase resolution by 4x
        const int highResSize = IconSizeInPixels * scaleFactor;

        using var highResBitmap = new Bitmap(highResSize, highResSize);
        using var graphics = Graphics.FromImage(highResBitmap);

        // Set up high-quality rendering
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

        // Clear background to transparent
        graphics.Clear(Color.Transparent);

        // Draw the number
        var text = number.ToString();
        var font = new Font(DefaultFontFamily, IconSizeInPixels * scaleFactor, _currentFontStyle, GraphicsUnit.Pixel);
        var size = graphics.MeasureString(text, font);
        var x = (highResSize - size.Width) / 2;
        var y = (highResSize - size.Height) / 2;

        graphics.DrawString(text, font, _currentBrush, x, y);

        // Scale down to the desired size and convert to icon
        using var finalBitmap = new Bitmap(highResBitmap, new Size(IconSizeInPixels, IconSizeInPixels));
        var handle = finalBitmap.GetHicon();
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