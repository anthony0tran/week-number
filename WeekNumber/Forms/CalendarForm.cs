using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WeekNumber.Forms;

public sealed class CalendarForm : Form
{
    // Visual constants for the card shell.
    private static readonly Color CardBackgroundColor = Color.FromArgb(0xF6, 0xF6, 0xF6);
    private static readonly Color CardBorderColor = Color.FromArgb(0xDE, 0xDE, 0xDE);
    private static readonly Color AccentColor = Color.FromArgb(0x1E, 0x6B, 0xD6);

    // Geometry constants for shell layout.
    private const int CornerRadius = 8;
    private const int OuterPadding = 8;
    private const int InnerPadding = 8;

    // Shell composition.
    private readonly DoubleBufferedPanel _cardPanel;
    private readonly CalendarControl _calendarControl;

    // Initializes the topmost borderless popup, hosts the calendar, and applies shell visuals.
    public CalendarForm()
    {
        var isoCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        isoCulture.DateTimeFormat.CalendarWeekRule = CalendarWeekRule.FirstFourDayWeek;
        isoCulture.DateTimeFormat.FirstDayOfWeek = DayOfWeek.Monday;
        Thread.CurrentThread.CurrentCulture = isoCulture;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = CardBackgroundColor;
        Padding = new Padding(OuterPadding);
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;

        _cardPanel = new DoubleBufferedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = CardBackgroundColor,
            Padding = new Padding(InnerPadding)
        };
        _cardPanel.Paint += OnCardPanelPaint;
        Controls.Add(_cardPanel);

        _calendarControl = new CalendarControl
        {
            Dock = DockStyle.Fill,
            BackColor = CardBackgroundColor,
            ForeColor = Color.FromArgb(0x1F, 0x1F, 0x1F),
            HeaderForeground = Color.FromArgb(0x1F, 0x1F, 0x1F),
            SecondaryForeground = Color.FromArgb(0x6B, 0x6B, 0x6B),
            Accent = AccentColor,
            CalendarDimensions = new Size(2, 1),
            ShowWeekNumbers = true,
            ShowToday = true,
            ShowTodayCircle = true,
        };
        try { _calendarControl.Font = new Font("Segoe UI Variable", 9f); }
        catch { _calendarControl.Font = new Font("Segoe UI", 9f); }

        _cardPanel.Controls.Add(_calendarControl);
        _calendarControl.HighlightIsoWeek(DateTime.Today);

        var preferred = _calendarControl.GetPreferredSize(Size.Empty);
        ClientSize = new Size(
            preferred.Width + _cardPanel.Padding.Horizontal,
            preferred.Height + _cardPanel.Padding.Vertical + 2);

        DwmInterop.SetRoundedCorners(Handle);

        Deactivate += (_, __) => Close();
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        Shown += (_, __) => UpdateFormRegion();
        SizeChanged += (_, __) => UpdateFormRegion();
    }

    // Shows the popup near the cursor, clamped to working area.
    public void ShowAtCursor()
    {
        Location = new Point(-2000, -2000);
        Visible = true;

        var preferred = _calendarControl.GetPreferredSize(Size.Empty);
        ClientSize = new Size(
            preferred.Width + _cardPanel.Padding.Horizontal,
            preferred.Height + _cardPanel.Padding.Vertical + 2);

        var cursor = Cursor.Position;
        var screen = Screen.FromPoint(cursor);
        var wa = screen.WorkingArea;

        int x = cursor.X - Width / 2;
        int y = wa.Bottom - Height;
        x = Math.Max(wa.Left, Math.Min(x, wa.Right - Width));
        y = Math.Max(wa.Top, y);

        Location = new Point(x, y);
        Activate();
    }

    // Paints the rounded card border.
    private void OnCardPanelPaint(object? sender, PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = _cardPanel.ClientRectangle;
        rect.Width -= 1;
        rect.Height -= 1;

        using var path = GraphicsUtil.CreateRoundedRect(rect, CornerRadius);
        using var pen = new Pen(CardBorderColor);
        e.Graphics.DrawPath(pen, path);
    }

    // Applies a rounded clipping region to the form.
    private void UpdateFormRegion()
    {
        var rect = new Rectangle(0, 0, Width, Height);
        using var path = GraphicsUtil.CreateRoundedRect(rect, CornerRadius);
        Region = new Region(path);
    }
}
