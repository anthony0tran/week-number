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
        var cursorPos = Cursor.Position;
        var screen = Screen.FromPoint(cursorPos);
        var workingArea = screen.WorkingArea;

        // Calculate position, keeping the form within screen bounds
        var x = Math.Min(cursorPos.X, workingArea.Right - Width);
        var y = Math.Min(cursorPos.Y, workingArea.Bottom - Height);

        // Ensure the form doesn't go off-screen to the left or top
        x = Math.Max(x, workingArea.Left);
        y = Math.Max(y, workingArea.Top);

        Location = new Point(x, y);
        Show();
        Activate();
    }
}