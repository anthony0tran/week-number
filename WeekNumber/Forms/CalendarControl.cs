
using System.ComponentModel;
using System.Globalization;
using System.Drawing.Drawing2D;

namespace WeekNumber.Forms;

internal sealed class CalendarControl : Control
{
    // Fixed rows per month grid.
    private const int FixedWeeksPerMonth = 6;

    // Layout metrics.
    private int _headerHeight = 28;
    private int _headerVertPadding = 8;
    private int _weekRowHeight = 20;
    private int _cellHeight = 24;
    private int _cellWidth = 34;
    private int _monthGap = 14;
    private int _weekColumnWidth = 36;
    private int _headerSidePadding = 26;
    private int _footerHeight = 30;
    private int _footerSeparatorGap = 6;
    private int _gridBottomGap = 4;
    private int _padLeft = 8;
    private int _padTop = 8;
    private int _padRight = 14;
    private int _padBottom = 10;

    // Theme colors.
    private static readonly Color HeaderBlueColor = Color.FromArgb(0x2B, 0x74, 0xE6);
    private static readonly Color WeekNumberColor = Color.FromArgb(0x7A, 0x58, 0x58);
    private static readonly Color TrailingDayColor = Color.FromArgb(0xCF, 0xCF, 0xD3);
    private static readonly Color TodayCircleColor = Color.FromArgb(0xB3, 0x1B, 0x1B);
    private static readonly Color DividerColor = Color.FromArgb(0xE6, 0xE6, 0xE6);
    private static readonly Color WeekHighlightFillColor = Color.FromArgb(0xE0, 0xEC, 0xFF);
    private static readonly Color WeekHighlightBorderColor = Color.FromArgb(0xAD, 0xCA, 0xFF);

    // Title vertical position ratio inside header band (0..1).
    private readonly float _headerTitleCenterRatio = 0.28f;

    // Today circle visuals.
    private const int TodayCircleDiameter = 26;
    private const float TodayCircleStroke = 2.0f;

    // Public configuration surface.
    [DefaultValue(typeof(Size), "2, 1")] public Size CalendarDimensions { get; init; } = new(2, 1);

    [DefaultValue(true)] public bool ShowWeekNumbers { get; init; } = true;

    [DefaultValue(true)] public bool ShowToday { get; init; } = true;

    [DefaultValue(true)] public bool ShowTodayCircle { get; init; } = true;

    [DefaultValue(typeof(Color), "Black")]
    public Color HeaderForeground { get; init; } = Color.FromArgb(0x1F, 0x1F, 0x1F);

    [DefaultValue(typeof(Color), "Gray")]
    public Color SecondaryForeground { get; init; } = Color.FromArgb(0x6B, 0x6B, 0x6B);

    [DefaultValue(typeof(Color), "0, 100, 210")]
    public Color Accent { get; init; } = Color.FromArgb(0x1E, 0x6B, 0xD6);

    // Current display month (first of month).
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DateTime DisplayMonth { get; private set; } = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

    // Cached hit rectangles.
    private Rectangle _prevChevronRect;
    private Rectangle _nextChevronRect;
    private Rectangle _todayBoxRect;
    private Rectangle _footerRect;

    // Week highlight anchor (ISO week start) and owning month (first of month).
    private DateTime? _highlightWeekStart;
    private DateTime? _highlightWeekMonth;

    // Culture used for names/formatting.
    private readonly CultureInfo _culture;

    // Sets ISO culture defaults and paints with double-buffering.
    public CalendarControl()
    {
        _culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        _culture.DateTimeFormat.CalendarWeekRule = CalendarWeekRule.FirstFourDayWeek;
        _culture.DateTimeFormat.FirstDayOfWeek = DayOfWeek.Monday;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint, true);

        TabStop = false;
        Cursor = Cursors.Default;

        MouseWheel += (_, e) =>
        {
            // Scroll up -> previous; scroll down -> next.
            DisplayMonth = DisplayMonth.AddMonths(e.Delta > 0 ? -1 : 1);
            Invalidate();
        };
    }

    // Updates the highlighted ISO week and repaints (programmatic use).
    // Owning month is required to avoid duplicating highlight across adjacent months.
    public void HighlightIsoWeek(DateTime anyDate, DateTime owningMonthFirstOfMonth)
    {
        _highlightWeekStart = StartOfIsoWeek(anyDate);
        _highlightWeekMonth = new DateTime(owningMonthFirstOfMonth.Year, owningMonthFirstOfMonth.Month, 1);
        Invalidate();
    }

    // Backwards-compatible overload: defaults ownership to the calendar month currently showing 'anyDate'.
    // If the control shows multiple months including 'anyDate', the month matching 'anyDate' gets ownership.
    public void HighlightIsoWeek(DateTime anyDate)
    {
        _highlightWeekStart = StartOfIsoWeek(anyDate);
        _highlightWeekMonth = new DateTime(anyDate.Year, anyDate.Month, 1);
        Invalidate();
    }

    // Scales all metrics with DPI changes.
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        base.ScaleControl(factor, specified);

        _headerHeight = (int)Math.Round(_headerHeight * factor.Height);
        _headerVertPadding = (int)Math.Round(_headerVertPadding * factor.Height);
        _weekRowHeight = (int)Math.Round(_weekRowHeight * factor.Height);
        _cellHeight = (int)Math.Round(_cellHeight * factor.Height);
        _cellWidth = (int)Math.Round(_cellWidth * factor.Width);
        _monthGap = (int)Math.Round(_monthGap * factor.Width);
        _weekColumnWidth = (int)Math.Round(_weekColumnWidth * factor.Width);
        _headerSidePadding = (int)Math.Round(_headerSidePadding * factor.Width);
        _footerHeight = (int)Math.Round(_footerHeight * factor.Height);
        _footerSeparatorGap = (int)Math.Round(_footerSeparatorGap * factor.Height);
        _gridBottomGap = (int)Math.Round(_gridBottomGap * factor.Height);
        _padLeft = (int)Math.Round(_padLeft * factor.Width);
        _padTop = (int)Math.Round(_padTop * factor.Height);
        _padRight = (int)Math.Round(_padRight * factor.Width);
        _padBottom = (int)Math.Round(_padBottom * factor.Height);
    }

    // Returns the preferred control size from current font/culture and layout metrics.
    public override Size GetPreferredSize(Size proposedSize)
    {
        using var g = CreateGraphics();
        ComputeMetrics(g);

        int across = CalendarDimensions.Width;
        int down = CalendarDimensions.Height;

        int weekColWidth = ShowWeekNumbers ? _weekColumnWidth : 0;
        int monthWidth = weekColWidth + (_cellWidth * 7);
        int monthHeightFixed = _headerHeight + _weekRowHeight + (_cellHeight * FixedWeeksPerMonth);

        int gridHeightAll = down * monthHeightFixed + (down - 1) * _monthGap;
        int totalWidth = _padLeft + _padRight + across * monthWidth + (across - 1) * _monthGap;
        int totalHeight = _padTop + gridHeightAll + _gridBottomGap + 1 + _footerSeparatorGap + _footerHeight +
                          _padBottom;

        return new Size(totalWidth, totalHeight);
    }

    // Paints the full calendar surface and aligns chevrons to the title center.
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        ComputeMetrics(g);

        using (var bg = new SolidBrush(BackColor))
            g.FillRectangle(bg, ClientRectangle);

        using var headerFont = new Font(Font.FontFamily, Font.Size + 0.5f, FontStyle.Regular);

        int headerTop = _padTop;
        int titleCenterY = headerTop + (int)Math.Round(_headerHeight * _headerTitleCenterRatio);

        // Chevron rectangles vertically centered to month title center.
        Size chevronSize = new Size(_headerSidePadding - 8, _headerHeight - 10);
        int chevronHalfH = chevronSize.Height / 2;

        _prevChevronRect = new Rectangle(
            _padLeft + 1,
            titleCenterY - chevronHalfH,
            chevronSize.Width,
            chevronSize.Height);

        _nextChevronRect = new Rectangle(
            Width - _padRight - _headerSidePadding + 7,
            titleCenterY - chevronHalfH,
            chevronSize.Width,
            chevronSize.Height);

        DrawChevron(g, _prevChevronRect, left: true);
        DrawChevron(g, _nextChevronRect, left: false);

        int across = CalendarDimensions.Width;
        int down = CalendarDimensions.Height;

        int weekColWidth = ShowWeekNumbers ? _weekColumnWidth : 0;
        int monthWidth = weekColWidth + (_cellWidth * 7);
        int monthHeightFixed = _headerHeight + _weekRowHeight + (_cellHeight * FixedWeeksPerMonth);

        var monthCursor = DisplayMonth;
        int yCursor = _padTop;

        for (int r = 0; r < down; r++)
        {
            int xCursor = _padLeft;

            for (int c = 0; c < across; c++)
            {
                var bounds = new Rectangle(xCursor, yCursor, monthWidth, monthHeightFixed);
                DrawMonth(g, bounds, monthCursor, headerFont);
                monthCursor = monthCursor.AddMonths(1);

                if (c < across - 1)
                {
                    int dividerX = bounds.Right + _monthGap / 2;
                    using var p = new Pen(DividerColor, 1f);
                    g.DrawLine(p, dividerX, bounds.Top + _headerHeight, dividerX, bounds.Bottom);
                }

                xCursor += monthWidth + (c < across - 1 ? _monthGap : 0);
            }

            yCursor += monthHeightFixed + (r < down - 1 ? _monthGap : 0);
        }

        int sepY = _padTop + (down * monthHeightFixed) + ((down - 1) * _monthGap) + _gridBottomGap;
        using (var p = new Pen(DividerColor, 1f))
            g.DrawLine(p, _padLeft, sepY, Width - _padRight, sepY);

        var footerRect = new Rectangle(
            _padLeft,
            sepY + _footerSeparatorGap,
            Width - (_padLeft + _padRight),
            _footerHeight);

        const int boxSize = 16;
        int boxY = footerRect.Y + (footerRect.Height - boxSize) / 2;
        _todayBoxRect = new Rectangle(footerRect.X, boxY, boxSize, boxSize);
        _footerRect = footerRect;

        using (var boxPen = new Pen(Accent, 2))
            g.DrawRectangle(boxPen, _todayBoxRect);

        var dtf = _culture.DateTimeFormat;
        var todayTextRect = new Rectangle(_todayBoxRect.Right + 8, footerRect.Y,
            footerRect.Width - (_todayBoxRect.Width + 8), footerRect.Height);

        TextRenderer.DrawText(g,
            $"Today: {DateTime.Today.ToString("dd-MM-yyyy", _culture)}",
            Font,
            todayTextRect,
            SecondaryForeground,
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.Left |
            TextFormatFlags.SingleLine |
            TextFormatFlags.NoPadding);
    }

    // Draws a single month header, weekday headers, grid, week numbers, highlights, and today circle.
    private void DrawMonth(Graphics g, Rectangle bounds, DateTime month, Font headerFont)
    {
        var dtf = _culture.DateTimeFormat;
        List<Rectangle> todayCircles = new();
        
        string title = $"{dtf.GetMonthName(month.Month)} {month.Year}";
        var titleSize = TextRenderer.MeasureText(g, title, headerFont, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        int titleCenterY = bounds.Y + (int)Math.Round(_headerHeight * _headerTitleCenterRatio);
        int titleTop = titleCenterY - titleSize.Height / 2;

        var headerTitleRect = new Rectangle(bounds.X, titleTop, bounds.Width, titleSize.Height);
        TextRenderer.DrawText(g, title, headerFont, headerTitleRect, HeaderForeground,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.Top |
            TextFormatFlags.SingleLine |
            TextFormatFlags.NoPadding);

        int weekColWidth = ShowWeekNumbers ? _weekColumnWidth : 0;
        var namesRect = new Rectangle(bounds.X, bounds.Y + _headerHeight, bounds.Width, _weekRowHeight);

        int colX = bounds.X;

        if (ShowWeekNumbers)
        {
            var wkRect = new Rectangle(colX, namesRect.Y, weekColWidth, namesRect.Height);
            TextRenderer.DrawText(g, "CW", Font, wkRect, SecondaryForeground,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine |
                TextFormatFlags.NoPadding);
            colX += weekColWidth;
        }

        for (int d = 0; d < 7; d++)
        {
            string name = dtf.AbbreviatedDayNames[((int)dtf.FirstDayOfWeek + d) % 7];
            var rect = new Rectangle(colX, namesRect.Y, _cellWidth, namesRect.Height);
            TextRenderer.DrawText(g, name, Font, rect, HeaderBlueColor,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine |
                TextFormatFlags.NoPadding);
            colX += _cellWidth;
        }

        DateTime firstOfMonth = new DateTime(month.Year, month.Month, 1);
        int offset = (((int)firstOfMonth.DayOfWeek - (int)dtf.FirstDayOfWeek) + 7) % 7;
        DateTime gridStart = firstOfMonth.AddDays(-offset);

        int weekColX = bounds.X;
        int gridXStart = bounds.X + weekColWidth;
        int gridY = bounds.Y + _headerHeight + _weekRowHeight;

        for (int row = 0; row < FixedWeeksPerMonth; row++)
        {
            DateTime rowWeekStart = StartOfIsoWeek(gridStart.AddDays(row * 7));

            // Only highlight if both the week start matches AND this month owns the highlight.
            if (_highlightWeekStart.HasValue &&
                _highlightWeekMonth.HasValue &&
                rowWeekStart == _highlightWeekStart.Value &&
                month.Date == _highlightWeekMonth.Value)
            {
                
                int left = bounds.X + 2;
                int right = bounds.Right - 2;


                var bar = Rectangle.FromLTRB(
                    left,
                    gridY,
                    right,
                    gridY + _cellHeight);


                using var fill = new SolidBrush(WeekHighlightFillColor);
                using var pen = new Pen(WeekHighlightBorderColor, 1f);

                g.FillRectangle(fill, bar);
                g.DrawRectangle(pen, bar.X, bar.Y, bar.Width - 1, bar.Height - 1);


            }

            if (ShowWeekNumbers)
            {
                int weekNumber = ISOWeek.GetWeekOfYear(rowWeekStart);
                var wkRect = new Rectangle(weekColX, gridY, weekColWidth, _cellHeight);
                TextRenderer.DrawText(g, weekNumber.ToString(), Font, wkRect, WeekNumberColor,
                    TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine |
                    TextFormatFlags.NoPadding);
            }

            int cx = gridXStart;
            for (int col = 0; col < 7; col++)
            {
                var day = gridStart.AddDays(row * 7 + col);
                var rect = new Rectangle(cx, gridY, _cellWidth, _cellHeight);

                bool inMonth = day.Month == month.Month;
                bool isToday = day.Date == DateTime.Today.Date;

                var numberColor = inMonth ? HeaderForeground : TrailingDayColor;

                TextRenderer.DrawText(g, day.Day.ToString(), Font, rect, numberColor,
                    TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine |
                    TextFormatFlags.NoPadding);

                // Draw "today" circle only if today exists AND belongs to this month.

                if (ShowToday && ShowTodayCircle && isToday && inMonth)
                {
                    todayCircles.Add(new Rectangle(
                        rect.X + (rect.Width - TodayCircleDiameter) / 2,
                        rect.Y + (rect.Height - TodayCircleDiameter) / 2,
                        TodayCircleDiameter,
                        TodayCircleDiameter));
                }


                cx += _cellWidth;
            }

            gridY += _cellHeight;
        }
        
        using var todayPen = new Pen(TodayCircleColor, TodayCircleStroke);
        foreach (var circle in todayCircles)
        {
            g.DrawEllipse(todayPen, circle);
        }

    }

    
    // Paints a chevron glyph inside a rectangle (left/right).
    private void DrawChevron(Graphics g, Rectangle rect, bool left)
    {
        using var p = new Pen(SecondaryForeground, 1.2f);
        var cx = rect.X + rect.Width / 2;
        var cy = rect.Y + rect.Height / 2;
        var size = Math.Max(4, rect.Height / 3);

        g.DrawLines(p,
            left
                ? new[] { new Point(cx + 2, cy - size), new Point(cx - 3, cy), new Point(cx + 2, cy + size) }
                : new[] { new Point(cx - 2, cy - size), new Point(cx + 3, cy), new Point(cx - 2, cy + size) });
    }

    // Measures text to update minimum cell/row widths/heights.
    private void ComputeMetrics(Graphics g)
    {
        var dtf = _culture.DateTimeFormat;

        var longestDay = "Wed";
        foreach (var s in dtf.AbbreviatedDayNames)
            if (s.Length > longestDay.Length)
                longestDay = s;

        var daySize = TextRenderer.MeasureText(g, longestDay, Font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        var weekSize = TextRenderer.MeasureText(g, "53", Font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        var titleProxy = TextRenderer.MeasureText(g, "September 2025", new Font(Font, FontStyle.Regular),
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        _cellWidth = Math.Max(_cellWidth, daySize.Width + 8);
        _weekColumnWidth = Math.Max(_weekColumnWidth, weekSize.Width + 10);
        _weekRowHeight = Math.Max(_weekRowHeight, daySize.Height + 4);
        _cellHeight = Math.Max(_cellHeight, daySize.Height + 8);

        _headerHeight = titleProxy.Height + 2 * _headerVertPadding;
    }

    // Handles mouse navigation and week-row hit testing.
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (_prevChevronRect.Contains(e.Location))
        {
            DisplayMonth = DisplayMonth.AddMonths(-1);
            Invalidate();
            return;
        }

        if (_nextChevronRect.Contains(e.Location))
        {
            DisplayMonth = DisplayMonth.AddMonths(1);
            Invalidate();
            return;
        }

        if (_footerRect.Contains(e.Location))
        {
            var today = DateTime.Today;
            DisplayMonth = new DateTime(today.Year, today.Month, 1);
            // Highlight today with ownership set to today's month to avoid duplicate highlight.
            HighlightIsoWeek(today, new DateTime(today.Year, today.Month, 1));
            Invalidate();
            return;
        }

        var across = CalendarDimensions.Width;
        var down = CalendarDimensions.Height;

        var weekColWidth = ShowWeekNumbers ? _weekColumnWidth : 0;
        var monthWidth = weekColWidth + (_cellWidth * 7);
        var monthHeightFixed = _headerHeight + _weekRowHeight + (_cellHeight * FixedWeeksPerMonth);

        var monthCursor = DisplayMonth;
        var yCursor = _padTop;

        for (var r = 0; r < down; r++)
        {
            var xCursor = _padLeft;

            for (var c = 0; c < across; c++)
            {
                var bounds = new Rectangle(xCursor, yCursor, monthWidth, monthHeightFixed);

                if (bounds.Contains(e.Location))
                {
                    var gridTop = bounds.Y + _headerHeight + _weekRowHeight;
                    var yRel = e.Y - gridTop;

                    if (yRel >= 0 && yRel < _cellHeight * FixedWeeksPerMonth)
                    {
                        var row = yRel / _cellHeight;

                        var dtf = _culture.DateTimeFormat;
                        DateTime firstOfMonth = new DateTime(monthCursor.Year, monthCursor.Month, 1);
                        var offset = ((int)firstOfMonth.DayOfWeek - (int)dtf.FirstDayOfWeek + 7) % 7;
                        DateTime gridStart = firstOfMonth.AddDays(-offset);

                        // Set highlight week and explicitly assign ownership to the clicked month.
                        var weekAnyDate = gridStart.AddDays(row * 7);
                        _highlightWeekStart = StartOfIsoWeek(weekAnyDate);
                        _highlightWeekMonth = firstOfMonth; // owning month = clicked month
                        Invalidate();
                        return;
                    }
                }

                xCursor += monthWidth + (c < across - 1 ? _monthGap : 0);
                monthCursor = monthCursor.AddMonths(1);
            }

            yCursor += monthHeightFixed + (r < down - 1 ? _monthGap : 0);
        }
    }

    // Returns the Monday of the ISO week for a given date.
    private static DateTime StartOfIsoWeek(DateTime date)
    {
        var dow = (int)date.DayOfWeek;
        if (dow == 0) dow = 7;
        return date.AddDays(1 - dow).Date;
    }
}
