using System.ComponentModel;
using System.Globalization;
using System.Drawing.Drawing2D;

namespace WeekNumber.Forms;

internal sealed class CalendarControl : Control
{
    #region Constants

    private const int FixedWeeksPerMonth = 6;
    private const int DaysPerWeek = 7;
    private const int TodayCircleDiameter = 26;
    private const float TodayCircleStroke = 1.8f;
    private const int TodayBoxSize = 16;
    private const int ChevronOffsetFromEdge = 8;
    private const int ChevronExtraOffset = 7;

    #endregion

    #region Layout Metrics (DPI-scaled)

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

    #endregion

    #region Theme Colors

    private static readonly Color HeaderBlueColor = Color.FromArgb(0x2B, 0x74, 0xE6);
    private static readonly Color WeekNumberColor = Color.FromArgb(0x7A, 0x58, 0x58);
    private static readonly Color TrailingDayColor = Color.FromArgb(0xCF, 0xCF, 0xD3);
    private static readonly Color TodayCircleColor = Color.FromArgb(0xB3, 0x1B, 0x1B);
    private static readonly Color DividerColor = Color.FromArgb(0xE6, 0xE6, 0xE6);
    private static readonly Color WeekHighlightFillColor = Color.FromArgb(0xE0, 0xEC, 0xFF);
    private static readonly Color WeekHighlightBorderColor = Color.FromArgb(0xAD, 0xCA, 0xFF);

    #endregion

    #region Configuration Properties

    private const float HeaderTitleCenterRatio = 0.28f;

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

    #endregion

    #region State Fields

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    private DateTime DisplayMonth { get; set; } = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    private Rectangle _prevChevronRect;
    private Rectangle _nextChevronRect;
    private Rectangle _todayBoxRect;
    private Rectangle _footerRect;

    private DateTime? _highlightWeekStart;
    private DateTime? _highlightWeekMonth;

    private readonly CultureInfo _culture;

    #endregion

    #region Initialization

    public CalendarControl()
    {
        _culture = CreateIsoCulture();
        ConfigureControlStyles();
        SetupEventHandlers();
    }

    private static CultureInfo CreateIsoCulture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.CalendarWeekRule = CalendarWeekRule.FirstFourDayWeek;
        culture.DateTimeFormat.FirstDayOfWeek = DayOfWeek.Monday;
        return culture;
    }

    private void ConfigureControlStyles()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint, true);

        TabStop = false;
        Cursor = Cursors.Default;
    }

    private void SetupEventHandlers()
    {
        MouseWheel += OnMouseWheelScroll;
    }

    private void OnMouseWheelScroll(object? sender, MouseEventArgs e)
    {
        var monthDelta = e.Delta > 0 ? -1 : 1;
        DisplayMonth = DisplayMonth.AddMonths(monthDelta);
        Invalidate();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Highlights an ISO week containing the specified date.
    /// The owning month is automatically set to the month of the provided date.
    /// </summary>
    public void HighlightIsoWeek(DateTime anyDate)
    {
        var monthFirstDay = new DateTime(anyDate.Year, anyDate.Month, 1);
        HighlightIsoWeek(anyDate, monthFirstDay);
    }

    #endregion

    #region Layout & Sizing

    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        base.ScaleControl(factor, specified);
        ScaleLayoutMetrics(factor);
    }

    private void ScaleLayoutMetrics(SizeF factor)
    {
        _headerHeight = ScaleVertical(_headerHeight, factor.Height);
        _headerVertPadding = ScaleVertical(_headerVertPadding, factor.Height);
        _weekRowHeight = ScaleVertical(_weekRowHeight, factor.Height);
        _cellHeight = ScaleVertical(_cellHeight, factor.Height);
        _cellWidth = ScaleHorizontal(_cellWidth, factor.Width);
        _monthGap = ScaleHorizontal(_monthGap, factor.Width);
        _weekColumnWidth = ScaleHorizontal(_weekColumnWidth, factor.Width);
        _headerSidePadding = ScaleHorizontal(_headerSidePadding, factor.Width);
        _footerHeight = ScaleVertical(_footerHeight, factor.Height);
        _footerSeparatorGap = ScaleVertical(_footerSeparatorGap, factor.Height);
        _gridBottomGap = ScaleVertical(_gridBottomGap, factor.Height);
        _padLeft = ScaleHorizontal(_padLeft, factor.Width);
        _padTop = ScaleVertical(_padTop, factor.Height);
        _padRight = ScaleHorizontal(_padRight, factor.Width);
        _padBottom = ScaleVertical(_padBottom, factor.Height);
    }

    private static int ScaleVertical(int value, float factor) => (int)Math.Round(value * factor);
    private static int ScaleHorizontal(int value, float factor) => (int)Math.Round(value * factor);

    public override Size GetPreferredSize(Size proposedSize)
    {
        using var g = CreateGraphics();
        ComputeMetrics(g);

        var layout = CalculateGridLayout();
        var monthDimensions = CalculateMonthDimensions();

        var totalWidth = CalculateTotalWidth(layout, monthDimensions);
        var totalHeight = CalculateTotalHeight(layout, monthDimensions);

        return new Size(totalWidth, totalHeight);
    }

    private MonthDimensions CalculateMonthDimensions()
    {
        var weekColWidth = ShowWeekNumbers ? _weekColumnWidth : 0;
        var monthWidth = weekColWidth + _cellWidth * DaysPerWeek;
        var monthHeight = _headerHeight + _weekRowHeight + _cellHeight * FixedWeeksPerMonth;

        return new MonthDimensions(monthWidth, monthHeight, weekColWidth);
    }

    private GridLayout CalculateGridLayout()
    {
        return new GridLayout(
            CalendarDimensions.Width,
            CalendarDimensions.Height,
            _monthGap);
    }

    private int CalculateTotalWidth(GridLayout layout, MonthDimensions dimensions)
    {
        var gridWidth = layout.Columns * dimensions.Width + (layout.Columns - 1) * layout.Gap;
        return _padLeft + gridWidth + _padRight;
    }

    private int CalculateTotalHeight(GridLayout layout, MonthDimensions dimensions)
    {
        var gridHeight = layout.Rows * dimensions.Height + (layout.Rows - 1) * layout.Gap;
        var footerSection = _gridBottomGap + 1 + _footerSeparatorGap + _footerHeight;
        return _padTop + gridHeight + footerSection + _padBottom;
    }

    private void ComputeMetrics(Graphics g)
    {
        var dtf = _culture.DateTimeFormat;

        var longestDayName = FindLongestDayName(dtf.AbbreviatedDayNames);
        var daySize = MeasureText(g, longestDayName, Font);
        var weekSize = MeasureText(g, "53", Font);
        var titleSize = MeasureText(g, "September 2025", Font);

        UpdateMetricsFromMeasurements(daySize, weekSize, titleSize);
    }

    private static string FindLongestDayName(string[] dayNames)
    {
        var longest = "Wed";
        foreach (var name in dayNames)
        {
            if (name.Length > longest.Length)
                longest = name;
        }

        return longest;
    }

    private static Size MeasureText(Graphics g, string text, Font font)
    {
        return TextRenderer.MeasureText(g, text, font,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
    }

    private void UpdateMetricsFromMeasurements(Size daySize, Size weekSize, Size titleSize)
    {
        _cellWidth = Math.Max(_cellWidth, daySize.Width + 8);
        _weekColumnWidth = Math.Max(_weekColumnWidth, weekSize.Width + 10);
        _weekRowHeight = Math.Max(_weekRowHeight, daySize.Height + 4);
        _cellHeight = Math.Max(_cellHeight, daySize.Height + 8);
        _headerHeight = titleSize.Height + 2 * _headerVertPadding;
    }

    #endregion

    #region Painting

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        ConfigureGraphicsQuality(g);
        ComputeMetrics(g);

        PaintBackground(g);

        using var headerFont = CreateHeaderFont();
        var layout = CalculateGridLayout();
        var dimensions = CalculateMonthDimensions();

        PaintNavigationChevrons(g);
        PaintMonthGrid(g, headerFont, layout, dimensions);
        PaintFooter(g, layout, dimensions);
    }

    private static void ConfigureGraphicsQuality(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
    }

    private void PaintBackground(Graphics g)
    {
        using var bg = new SolidBrush(BackColor);
        g.FillRectangle(bg, ClientRectangle);
    }

    private Font CreateHeaderFont()
    {
        return new Font(Font.FontFamily, Font.Size + 0.5f, FontStyle.Regular);
    }

    private void PaintNavigationChevrons(Graphics g)
    {
        var titleCenterY = _padTop + (int)Math.Round(_headerHeight * HeaderTitleCenterRatio);
        var chevronSize = new Size(_headerSidePadding - ChevronOffsetFromEdge, _headerHeight - 10);
        var chevronHalfHeight = chevronSize.Height / 2;

        _prevChevronRect = new Rectangle(
            _padLeft + 1,
            titleCenterY - chevronHalfHeight,
            chevronSize.Width,
            chevronSize.Height);

        _nextChevronRect = new Rectangle(
            Width - _padRight - _headerSidePadding + ChevronExtraOffset,
            titleCenterY - chevronHalfHeight,
            chevronSize.Width,
            chevronSize.Height);

        DrawChevron(g, _prevChevronRect, isLeftPointing: true);
        DrawChevron(g, _nextChevronRect, isLeftPointing: false);
    }

    private void PaintMonthGrid(Graphics g, Font headerFont, GridLayout layout, MonthDimensions dimensions)
    {
        var monthCursor = DisplayMonth;
        var yCursor = _padTop;

        for (var row = 0; row < layout.Rows; row++)
        {
            var xCursor = _padLeft;

            for (var col = 0; col < layout.Columns; col++)
            {
                var bounds = new Rectangle(xCursor, yCursor, dimensions.Width, dimensions.Height);
                DrawMonth(g, bounds, monthCursor, headerFont);
                monthCursor = monthCursor.AddMonths(1);

                if (col < layout.Columns - 1)
                {
                    PaintVerticalDivider(g, bounds);
                }

                xCursor += dimensions.Width + (col < layout.Columns - 1 ? layout.Gap : 0);
            }

            yCursor += dimensions.Height + (row < layout.Rows - 1 ? layout.Gap : 0);
        }
    }

    private void PaintVerticalDivider(Graphics g, Rectangle monthBounds)
    {
        var dividerX = monthBounds.Right + _monthGap / 2;
        using var pen = new Pen(DividerColor, 1f);
        g.DrawLine(pen, dividerX, monthBounds.Top + _headerHeight, dividerX, monthBounds.Bottom);
    }

    private void PaintFooter(Graphics g, GridLayout layout, MonthDimensions dimensions)
    {
        var separatorY = CalculateFooterSeparatorY(layout, dimensions);
        PaintHorizontalSeparator(g, separatorY);

        var footerRect = new Rectangle(
            _padLeft,
            separatorY + _footerSeparatorGap,
            Width - (_padLeft + _padRight),
            _footerHeight);

        _footerRect = footerRect;
        PaintTodayIndicator(g, footerRect);
    }

    private int CalculateFooterSeparatorY(GridLayout layout, MonthDimensions dimensions)
    {
        return _padTop + layout.Rows * dimensions.Height + (layout.Rows - 1) * layout.Gap + _gridBottomGap;
    }

    private void PaintHorizontalSeparator(Graphics g, int y)
    {
        using var pen = new Pen(DividerColor, 1f);
        g.DrawLine(pen, _padLeft, y, Width - _padRight, y);
    }

    private void PaintTodayIndicator(Graphics g, Rectangle footerRect)
    {
        var boxY = footerRect.Y + (footerRect.Height - TodayBoxSize) / 2;
        _todayBoxRect = new Rectangle(footerRect.X, boxY, TodayBoxSize, TodayBoxSize);

        using (var boxPen = new Pen(Accent, 2))
        {
            g.DrawRectangle(boxPen, _todayBoxRect);
        }

        var todayText = $"Today: {DateTime.Today:dd-MM-yyyy}";
        var textRect = footerRect with
        {
            X = _todayBoxRect.Right + 8, Width = footerRect.Width - (_todayBoxRect.Width + 8)
        };

        TextRenderer.DrawText(g, todayText, Font, textRect, SecondaryForeground,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }

    #endregion

    #region Month Rendering

    private void DrawMonth(Graphics g, Rectangle bounds, DateTime month, Font headerFont)
    {
        DrawMonthHeader(g, bounds, month, headerFont);
        DrawWeekdayHeaders(g, bounds);

        var gridStartDate = CalculateGridStartDate(month);
        DrawMonthGrid(g, bounds, month, gridStartDate);
    }

    private void DrawMonthHeader(Graphics g, Rectangle bounds, DateTime month, Font headerFont)
    {
        var title = FormatMonthTitle(month);
        var titleSize = MeasureText(g, title, headerFont);
        var titleCenterY = bounds.Y + (int)Math.Round(_headerHeight * HeaderTitleCenterRatio);
        var titleTop = titleCenterY - titleSize.Height / 2;

        var headerRect = bounds with { Y = titleTop, Height = titleSize.Height };

        TextRenderer.DrawText(g, title, headerFont, headerRect, HeaderForeground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }

    private string FormatMonthTitle(DateTime month)
    {
        var monthName = _culture.DateTimeFormat.GetMonthName(month.Month);
        return $"{monthName} {month.Year}";
    }

    private void DrawWeekdayHeaders(Graphics g, Rectangle bounds)
    {
        var weekColWidth = ShowWeekNumbers ? _weekColumnWidth : 0;
        var headerRect = bounds with
        {
            Y = bounds.Y + _headerHeight,
            Height = _weekRowHeight
        };

        var xCursor = bounds.X;

        if (ShowWeekNumbers)
        {
            DrawWeekNumberHeader(g, headerRect with { X = xCursor, Width = weekColWidth });
            xCursor += weekColWidth;
        }

        DrawDayNameHeaders(g, headerRect, xCursor);
    }

    private void DrawWeekNumberHeader(Graphics g, Rectangle rect)
    {
        TextRenderer.DrawText(g, "CW", Font, rect, SecondaryForeground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }

    private void DrawDayNameHeaders(Graphics g, Rectangle headerRect, int startX)
    {
        var dtf = _culture.DateTimeFormat;
        var xCursor = startX;

        for (var day = 0; day < DaysPerWeek; day++)
        {
            var dayIndex = ((int)dtf.FirstDayOfWeek + day) % DaysPerWeek;
            var dayName = dtf.AbbreviatedDayNames[dayIndex];
            var rect = headerRect with { X = xCursor, Width = _cellWidth };

            TextRenderer.DrawText(g, dayName, Font, rect, HeaderBlueColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            xCursor += _cellWidth;
        }
    }

    private void DrawMonthGrid(Graphics g, Rectangle bounds, DateTime month, DateTime gridStart)
    {
        var weekColWidth = ShowWeekNumbers ? _weekColumnWidth : 0;
        var gridY = bounds.Y + _headerHeight + _weekRowHeight;
        var todayCircles = new List<Rectangle>();

        for (var row = 0; row < FixedWeeksPerMonth; row++)
        {
            var weekStartDate = gridStart.AddDays(row * DaysPerWeek);
            var isoWeekStart = StartOfIsoWeek(weekStartDate);

            DrawWeekHighlight(g, bounds, month, gridY, isoWeekStart);
            DrawWeekNumber(g, bounds.X, gridY, weekColWidth, isoWeekStart);
            DrawWeekDays(g, bounds.X + weekColWidth, gridY, month, weekStartDate, todayCircles);

            gridY += _cellHeight;
        }

        DrawTodayCircles(g, todayCircles);
    }

    private void DrawWeekHighlight(Graphics g, Rectangle bounds, DateTime month, int y, DateTime weekStart)
    {
        if (!ShouldHighlightWeek(weekStart, month))
            return;

        var highlightRect = Rectangle.FromLTRB(
            bounds.X + 2,
            y,
            bounds.Right - 2,
            y + _cellHeight);

        using var fill = new SolidBrush(WeekHighlightFillColor);
        using var pen = new Pen(WeekHighlightBorderColor, 1f);

        g.FillRectangle(fill, highlightRect);
        g.DrawRectangle(pen, highlightRect.X, highlightRect.Y,
            highlightRect.Width - 1, highlightRect.Height - 1);
    }

    private bool ShouldHighlightWeek(DateTime weekStart, DateTime month)
    {
        return _highlightWeekStart.HasValue &&
               _highlightWeekMonth.HasValue &&
               weekStart == _highlightWeekStart.Value &&
               month.Date == _highlightWeekMonth.Value;
    }

    private void DrawWeekNumber(Graphics g, int x, int y, int weekColWidth, DateTime weekStart)
    {
        if (!ShowWeekNumbers)
            return;

        var weekNumber = ISOWeek.GetWeekOfYear(weekStart);
        var rect = new Rectangle(x, y, weekColWidth, _cellHeight);

        TextRenderer.DrawText(g, weekNumber.ToString(), Font, rect, WeekNumberColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }

    private void DrawWeekDays(Graphics g, int startX, int y, DateTime month,
        DateTime weekStart, List<Rectangle> todayCircles)
    {
        var xCursor = startX;

        for (var col = 0; col < DaysPerWeek; col++)
        {
            var day = weekStart.AddDays(col);
            var rect = new Rectangle(xCursor, y, _cellWidth, _cellHeight);

            DrawDayNumber(g, rect, day, month);

            if (IsTodayInMonth(day, month))
            {
                todayCircles.Add(CalculateTodayCircleRect(rect));
            }

            xCursor += _cellWidth;
        }
    }

    private void DrawDayNumber(Graphics g, Rectangle rect, DateTime day, DateTime month)
    {
        var isInMonth = day.Month == month.Month;
        var color = isInMonth ? HeaderForeground : TrailingDayColor;

        TextRenderer.DrawText(g, day.Day.ToString(), Font, rect, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }

    private bool IsTodayInMonth(DateTime day, DateTime month)
    {
        return ShowToday && ShowTodayCircle &&
               day.Date == DateTime.Today.Date &&
               day.Month == month.Month;
    }

    private static Rectangle CalculateTodayCircleRect(Rectangle cellRect)
    {
        return new Rectangle(
            cellRect.X + (cellRect.Width - TodayCircleDiameter) / 2,
            cellRect.Y + (cellRect.Height - TodayCircleDiameter) / 2,
            TodayCircleDiameter,
            TodayCircleDiameter);
    }

    private static void DrawTodayCircles(Graphics g, List<Rectangle> todayCircles)
    {
        if (todayCircles.Count == 0)
            return;

        using var pen = new Pen(TodayCircleColor, TodayCircleStroke);
        foreach (var circle in todayCircles)
        {
            g.DrawEllipse(pen, circle);
        }
    }

    private DateTime CalculateGridStartDate(DateTime month)
    {
        var firstOfMonth = new DateTime(month.Year, month.Month, 1);
        var dtf = _culture.DateTimeFormat;
        var offset = ((int)firstOfMonth.DayOfWeek - (int)dtf.FirstDayOfWeek + 7) % 7;
        return firstOfMonth.AddDays(-offset);
    }

    #endregion

    #region Helper Drawing Methods

    private void DrawChevron(Graphics g, Rectangle rect, bool isLeftPointing)
    {
        using var pen = new Pen(SecondaryForeground, 1.2f);
        var centerX = rect.X + rect.Width / 2;
        var centerY = rect.Y + rect.Height / 2;
        var arrowSize = Math.Max(4, rect.Height / 3);

        Point[] points = isLeftPointing
            ? new[]
            {
                new Point(centerX + 2, centerY - arrowSize),
                new Point(centerX - 3, centerY),
                new Point(centerX + 2, centerY + arrowSize)
            }
            : new[]
            {
                new Point(centerX - 2, centerY - arrowSize),
                new Point(centerX + 3, centerY),
                new Point(centerX - 2, centerY + arrowSize)
            };

        g.DrawLines(pen, points);
    }

    #endregion

    #region Mouse Interaction

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (TryHandleNavigationClick(e.Location))
            return;

        if (TryHandleTodayClick(e.Location))
            return;

        TryHandleWeekClick(e.Location);
    }

    private bool TryHandleNavigationClick(Point location)
    {
        if (_prevChevronRect.Contains(location))
        {
            NavigateToPreviousMonth();
            return true;
        }

        if (!_nextChevronRect.Contains(location)) return false;
        NavigateToNextMonth();
        return true;
    }

    private void NavigateToPreviousMonth()
    {
        DisplayMonth = DisplayMonth.AddMonths(-1);
        Invalidate();
    }

    private void NavigateToNextMonth()
    {
        DisplayMonth = DisplayMonth.AddMonths(1);
        Invalidate();
    }

    private bool TryHandleTodayClick(Point location)
    {
        if (!_footerRect.Contains(location))
            return false;

        var today = DateTime.Today;
        var todayMonth = new DateTime(today.Year, today.Month, 1);

        DisplayMonth = todayMonth;
        HighlightIsoWeek(today, todayMonth);
        Invalidate();

        return true;
    }

    private void TryHandleWeekClick(Point location)
    {
        var layout = CalculateGridLayout();
        var dimensions = CalculateMonthDimensions();
        var monthCursor = DisplayMonth;
        var yCursor = _padTop;

        for (var row = 0; row < layout.Rows; row++)
        {
            var xCursor = _padLeft;

            for (var col = 0; col < layout.Columns; col++)
            {
                var monthBounds = new Rectangle(xCursor, yCursor, dimensions.Width, dimensions.Height);

                if (TryHandleMonthWeekClick(location, monthBounds, monthCursor))
                    return;

                xCursor += dimensions.Width + (col < layout.Columns - 1 ? layout.Gap : 0);
                monthCursor = monthCursor.AddMonths(1);
            }

            yCursor += dimensions.Height + (row < layout.Rows - 1 ? layout.Gap : 0);
        }
    }

    private bool TryHandleMonthWeekClick(Point location, Rectangle monthBounds, DateTime month)
    {
        if (!monthBounds.Contains(location))
            return false;

        var gridTop = monthBounds.Y + _headerHeight + _weekRowHeight;
        var relativeY = location.Y - gridTop;
        var gridHeight = _cellHeight * FixedWeeksPerMonth;

        if (relativeY < 0 || relativeY >= gridHeight)
            return false;

        var clickedRow = relativeY / _cellHeight;
        var gridStart = CalculateGridStartDate(month);
        var weekDate = gridStart.AddDays(clickedRow * DaysPerWeek);

        HighlightIsoWeek(weekDate, month);
        Invalidate();

        return true;
    }

    private void HighlightIsoWeek(DateTime anyDate, DateTime owningMonthFirstOfMonth)
    {
        _highlightWeekStart = StartOfIsoWeek(anyDate);
        _highlightWeekMonth = new DateTime(owningMonthFirstOfMonth.Year, owningMonthFirstOfMonth.Month, 1);
        Invalidate();
    }

    #endregion

    #region Utility Methods

    private static DateTime StartOfIsoWeek(DateTime date)
    {
        var dayOfWeek = (int)date.DayOfWeek;
        if (dayOfWeek == 0) // Sunday
            dayOfWeek = 7;

        return date.AddDays(1 - dayOfWeek).Date;
    }

    #endregion

    #region Helper Structures

    private readonly record struct GridLayout(int Columns, int Rows, int Gap);

    private readonly record struct MonthDimensions(int Width, int Height, int WeekColumnWidth);

    #endregion
}