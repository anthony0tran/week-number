using System.Drawing.Drawing2D;
using System.Globalization;

namespace WeekNumber.Forms;

public sealed class CalendarForm : Form
{
    private static readonly Color CardBackgroundColor = Color.FromArgb(0xF6, 0xF6, 0xF6);
    private static readonly Color AccentColor = Color.FromArgb(0x1E, 0x6B, 0xD6);

    private const int CornerRadius = 10;
    private const int InnerPadding = 8;

    private readonly DoubleBufferedPanel _cardPanel;
    private readonly CalendarControl _calendarControl;

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
        Padding = Padding.Empty;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;

        _cardPanel = new DoubleBufferedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = CardBackgroundColor,
            Padding = new Padding(InnerPadding)
        };
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
            ShowTodayCircle = true
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

        Deactivate += (_, _) => Close();
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
    }

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
}