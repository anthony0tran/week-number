using System.Globalization;

namespace WeekNumber;

public sealed class CalendarForm : Form
{
    public CalendarForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        
        Padding = new Padding(0);

        // Configure the current thread's culture for ISO 8601 weeks
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.CalendarWeekRule = CalendarWeekRule.FirstFourDayWeek;
        culture.DateTimeFormat.FirstDayOfWeek = DayOfWeek.Monday;
        Thread.CurrentThread.CurrentCulture = culture;

        var calendar = new MonthCalendar
        {
            MaxSelectionCount = 1,
            ShowToday = true,
            ShowTodayCircle = true,
            CalendarDimensions = new Size(2, 1),
            ShowWeekNumbers = true,
            Margin = new Padding(0)
        };

        Controls.Add(calendar);

        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        calendar.Location = Point.Empty;

        // Close the form when the user clicks outside of it
        Deactivate += (_, _) => Close();
    }

    public void ShowAtCursor()
    {
        StartPosition = FormStartPosition.Manual;

        // First make the form visible but off-screen to allow layout to complete
        Location = new Point(-2000, -2000);
        Visible = true;

        // Now get the screen info
        var cursorPosition = Cursor.Position;
        var screen = Screen.FromPoint(cursorPosition);
        var workingArea = screen.WorkingArea;

        // Position horizontally centered at cursor position
        var x = cursorPosition.X - Width / 2;
        // Keep y at the bottom of the screen (unchanged)
        var y = workingArea.Bottom - Height;

        // Ensure it stays within view
        x = Math.Max(workingArea.Left, x);
        x = Math.Min(workingArea.Right - Width, x);
        y = Math.Max(workingArea.Top, y);

        Location = new Point(x, y);
        Activate();
    }
}