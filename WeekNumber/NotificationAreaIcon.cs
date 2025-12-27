using System.Drawing;
using System.Globalization;
using Microsoft.Win32;
using WeekNumber.Helpers;
using System.Reflection;
using System.IO;

namespace WeekNumber;

public sealed class NotificationAreaIcon : IDisposable
{
    private static readonly Lazy<NotificationAreaIcon> _instance = new(() => new NotificationAreaIcon(new IconFactory()));
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu = new();
    private readonly WeekNumber _weekNumber = new();
    private readonly IIconFactory _iconFactory;
    private bool _disposed;
    private const int IconSizeInPixels = 32;
    private static FontStyle _currentFontStyle = (FontStyle)Properties.Settings.Default.SelectedFontStyle;
    private Brush _currentBrush = BrushHelper.GetBrushFromColor(Properties.Settings.Default.SelectedColor);
    private const string DefaultFontFamily = "Arial";
    private Font _font = new(DefaultFontFamily, IconSizeInPixels, _currentFontStyle, GraphicsUnit.Pixel);

    public static NotificationAreaIcon Instance => _instance.Value;
    
    internal NotificationAreaIcon(IIconFactory iconFactory)
    {
        _iconFactory = iconFactory;

        var exitMenuItem = new ToolStripMenuItem("Exit", null, MenuExit_Click);
        var aboutMenuItem = new ToolStripMenuItem("About", null, MenuAbout_Click);
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
        _contextMenu.Items.Add(aboutMenuItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(exitMenuItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = _iconFactory.CreateNumberIcon(_weekNumber.Number, _font, _currentBrush, IconSizeInPixels),
            Text = $"Last updated on: {_weekNumber.LastUpdated.ToString("g", new CultureInfo("nl-NL"))}",
            Visible = true,
            ContextMenuStrip = _contextMenu
        };

        StartupManager.UpdateRegistryKey();

        _notifyIcon.MouseClick += NotifyIcon_LeftMouseClick;
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

    private static void MenuStartup_Click(object? sender, EventArgs e)
    {
        var menuItem = (ToolStripMenuItem)sender!;
        menuItem.Checked = !menuItem.Checked;
        StartupManager.SetStartup(menuItem.Checked);
    }

    private static string GetAppVersion()
    {
        var fileVer = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyFileVersionAttribute>()
            ?.Version;

        if (!string.IsNullOrWhiteSpace(fileVer))
        {
            var parts = fileVer.Split('.');
            if (parts.Length >= 3)
                return string.Join('.', parts[0], parts[1], parts[2]);
            return fileVer;
        }

        var infoVer = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(infoVer))
        {
            var plusIndex = infoVer.IndexOf('+');
            var ver = plusIndex >= 0 ? infoVer.Substring(0, plusIndex) : infoVer;
            var parts = ver.Split('.');
            if (parts.Length >= 3)
                return string.Join('.', parts[0], parts[1], parts[2]);
            return ver;
        }

        var prodVerParts = Application.ProductVersion.Split('.');
        if (prodVerParts.Length >= 3)
            return string.Join('.', prodVerParts[0], prodVerParts[1], prodVerParts[2]);
        return Application.ProductVersion;
    }

private static Form? _aboutForm;

private static void MenuAbout_Click(object? sender, EventArgs e)
{
    // If an instance exists and is still alive, just bring it to front and return
    if (_aboutForm is { IsDisposed: false } && _aboutForm.Visible)
    {
        if (_aboutForm.WindowState == FormWindowState.Minimized)
            _aboutForm.WindowState = FormWindowState.Normal;

        _aboutForm.Activate();
        _aboutForm.BringToFront();
        return;
    }

    // Otherwise create a new one
    string version = GetAppVersion();
    string iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "AppIcon.ico");

    var about = new Form
    {
        Text = $"WeekNumber {version}",
        Size = new Size(300, 220),
        FormBorderStyle = FormBorderStyle.FixedDialog,
        StartPosition = FormStartPosition.CenterScreen,
        MaximizeBox = false,
        MinimizeBox = false,
        ShowIcon = false,
        ShowInTaskbar = false
    };

    // Track this instance
    _aboutForm = about;

    // When it closes, clear the reference
    about.FormClosed += (_, __) =>
    {
        about.Dispose();
        if (ReferenceEquals(_aboutForm, about))
            _aboutForm = null;
    };

    Image bodyImage = File.Exists(iconPath)
        ? new Icon(iconPath, new Size(128, 128)).ToBitmap()
        : SystemIcons.Application.ToBitmap();

    var picture = new PictureBox
    {
        Size = new Size(128, 128),
        SizeMode = PictureBoxSizeMode.Zoom,
        Image = bodyImage,
        Margin = new Padding(12, 12, 16, 12)
    };

    float baseSize = SystemFonts.MessageBoxFont.Size;
    var link = new LinkLabel
    {
        Text = "Github",
        AutoSize = false,
        Size = new Size(240, 128),
        Tag = "https://github.com/anthony0tran/week-number",
        Font = new Font(SystemFonts.MessageBoxFont.FontFamily, baseSize + 4f, FontStyle.Bold),
        Margin = new Padding(0, 12, 12, 12),
        LinkBehavior = LinkBehavior.HoverUnderline,
        TextAlign = ContentAlignment.MiddleLeft
    };
    link.LinkClicked += (s, args) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = (string)link.Tag!,
            UseShellExecute = true
        });

    var flow = new FlowLayoutPanel
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = false,
        Padding = new Padding(12),
        AutoSize = false
    };

    flow.Controls.Add(picture);
    flow.Controls.Add(link);
    about.Controls.Add(flow);

    // If you want modal, use ShowDialog(owner). If modeless, use Show(owner)
    // Modal:
    about.ShowDialog(); // or pass an owner: about.ShowDialog(Form.ActiveForm);
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
        _notifyIcon.MouseClick -= NotifyIcon_LeftMouseClick;
    }

    private static void MenuExit_Click(object? sender, EventArgs e)
    {
        Application.Exit();
    }

    internal void UpdateIcon()
    {
        _notifyIcon.Icon = _iconFactory.CreateNumberIcon(_weekNumber.Number, _font, _currentBrush, IconSizeInPixels);
    }

    internal void UpdateText()
    {
        _notifyIcon.Text = $"Last updated on: {_weekNumber.LastUpdated.ToString("g", new CultureInfo("nl-NL"))}";
    }

    internal NotifyIcon NotifyIcon => _notifyIcon;
    
    public void Dispose()
    {
        if (_disposed)
            return;

        _notifyIcon.Dispose();
        _disposed = true;
    }
}
