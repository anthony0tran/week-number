﻿using System.Globalization;

namespace WeekNumber;

public class CalendarForm : Form
{
    public CalendarForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;

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
            CalendarDimensions = new Size(1, 1),
            ShowWeekNumbers = true
        };

        Controls.Add(calendar);
        var calendarSize = calendar.PreferredSize;
        ClientSize = calendarSize with
        {
            Width = calendarSize.Width + 150,
            Height = calendarSize.Height + 10
        };

        calendar.Location = Point.Empty;
        
        // Close the form when the user clicks outside of it
        Deactivate += (_, _) => Close();
    }

    public void ShowAtCursor()
    {
        StartPosition = FormStartPosition.Manual;
        var cursorPosition = Cursor.Position;
        var screen = Screen.FromPoint(cursorPosition);
        var workingArea = screen.WorkingArea;

        // Calculate initial position at cursor
        var x = Math.Max(workingArea.Left, Math.Min(cursorPosition.X, workingArea.Right - Width));
        var y = Math.Max(workingArea.Top, Math.Min(cursorPosition.Y, workingArea.Bottom - Height));

        // Set the location and show the form
        Location = new Point(x, y);
        Visible = true;
        Activate();
    }
}