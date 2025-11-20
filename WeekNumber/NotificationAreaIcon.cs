using System.Globalization;
using Microsoft.Win32;
using WeekNumber.Helpers;

namespace WeekNumber;

public sealed class NotificationAreaIcon : IDisposable
{
    private static readonly Lazy<NotificationAreaIcon> _instance = new(() => new NotificationAreaIcon(new IconFactory(), new StartupManager(new RegistryProvider(), new ExecutablePathProvider())));
    private readonly ContextMenuStrip _contextMenu = new();
    private readonly WeekNumber _weekNumber = new();
    private readonly IIconFactory _iconFactory;
    private readonly StartupManager _startupManager;
    private bool _disposed;
    private const int IconSizeInPixels = 32;
    private static FontStyle _currentFontStyle = (FontStyle)Properties.Settings.Default.SelectedFontStyle;
    private Brush _currentBrush = BrushHelper.GetBrushFromColor(Properties.Settings.Default.SelectedColor);
    private const string DefaultFontFamily = "Arial";
    private Font _font = new(DefaultFontFamily, IconSizeInPixels, _currentFontStyle, GraphicsUnit.Pixel);

    public static NotificationAreaIcon Instance => _instance.Value;
    
    internal NotificationAreaIcon(IIconFactory iconFactory, StartupManager startupManager)
    {
        _iconFactory = iconFactory;
        _startupManager = startupManager;

        var exitMenuItem = new ToolStripMenuItem("Exit", null, MenuExit_Click);
        var startupMenuItem = new ToolStripMenuItem("Run at Startup", null, MenuStartup_Click)
        {
            Checked = _startupManager.IsStartupEnabled()
        };
        var colorPickerMenuItem = new ToolStripMenuItem("Change Color", null, (_, _) =>
        {
            using ColorDialog colorDialog = new();
            colorDialog.AllowFullOpen = true;
            colorDialog.AnyColor = true;
            colorDialog.FullOpen = true;
            colorDialog.CustomColors = [0xFF0000, 0x00FF00, 0x0000FF, 0xFFFF00, 0xFF00FF, 0x00FFFF];

            if (colorDialog.ShowDialog() != DialogResult.OK)
                return;

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
                Checked = _currentFontStyle.HasFlag(style)
            };

            styleItem.CheckedChanged += (_, _) =>
            {
                if (styleItem.Checked)
                    _currentFontStyle |= style;
                else
                    _currentFontStyle &= ~style;

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

        NotifyIcon = new NotifyIcon
        {
            Icon = _iconFactory.CreateNumberIcon(_weekNumber.Number, _font, _currentBrush, IconSizeInPixels),
            Text = $"Last updated on: {_weekNumber.LastUpdated.ToString("g", new CultureInfo("nl-NL"))}",
            Visible = true,
            ContextMenuStrip = _contextMenu
        };

        _startupManager.UpdateRegistryKey();

        NotifyIcon.MouseClick += NotifyIcon_LeftMouseClick;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Application.ApplicationExit += OnApplicationExit;
    }

    private void NotifyIcon_LeftMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        _weekNumber.UpdateNumber();
        UpdateIcon();
        UpdateText();

        var calendarForm = new CalendarForm();
        calendarForm.ShowAtCursor();
    }

    private void MenuStartup_Click(object? sender, EventArgs e)
    {
        var menuItem = (ToolStripMenuItem)sender!;
        menuItem.Checked = !menuItem.Checked;
        _startupManager.SetStartup(menuItem.Checked);
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume)
            return;

        _weekNumber.UpdateNumber();
        UpdateIcon();
        UpdateText();
    }

    private void OnApplicationExit(object? sender, EventArgs e)
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        Application.ApplicationExit -= OnApplicationExit;
        NotifyIcon.MouseClick -= NotifyIcon_LeftMouseClick;
    }

    private static void MenuExit_Click(object? sender, EventArgs e)
    {
        Application.Exit();
    }

    internal void UpdateIcon()
    {
        NotifyIcon.Icon = _iconFactory.CreateNumberIcon(_weekNumber.Number, _font, _currentBrush, IconSizeInPixels);
    }

    internal void UpdateText()
    {
        NotifyIcon.Text = $"Last updated on: {_weekNumber.LastUpdated.ToString("g", new CultureInfo("nl-NL"))}";
    }

    internal NotifyIcon NotifyIcon { get; }

    public void Dispose()
    {
        if (_disposed)
            return;

        NotifyIcon.Dispose();
        _disposed = true;
    }
}
