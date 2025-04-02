namespace WeekNumber;

public class CalendarForm : Form
{
    private readonly MonthCalendar _calendar;

    public CalendarForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;

        _calendar = new MonthCalendar
        {
            MaxSelectionCount = 1,
            ShowToday = true,
            ShowTodayCircle = true,
            CalendarDimensions = new Size(1, 1),
            ShowWeekNumbers = true
        };

        Controls.Add(_calendar);
        var calendarSize = _calendar.PreferredSize;
        ClientSize = calendarSize with
        {
            Width = calendarSize.Width + 75,
            Height = calendarSize.Height + 7
        };

        _calendar.Location = Point.Empty;  // Position at top-left of form
        Deactivate += (_, _) => Close();
    }

    public void ShowAtCursor()
    {
        StartPosition = FormStartPosition.Manual;
        var screen = Screen.FromPoint(Cursor.Position);
        var bounds = screen.Bounds;
        
        var x = bounds.Right - Width - 10;
        var y = bounds.Bottom - Height - 40;

        Location = new Point(x, y);
        Visible = true;
        Activate();
    }
}