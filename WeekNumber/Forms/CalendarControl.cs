using System.ComponentModel;
using System.Globalization;
using System.Drawing.Drawing2D;

namespace WeekNumber.Forms;

internal sealed class CalendarControl : Control
{
    private const int FixedWeeksPerMonth = 6;

    // ── Fixed pixel metrics at 96 DPI ────────────────────────────────────────
    private const int B_HeaderVertPadding   = 8;
    private const int B_WeekRowHeight       = 20;
    private const int B_CellHeight          = 24;
    private const int B_CellWidth           = 34;
    private const int B_MonthGap            = 14;
    private const int B_WeekColumnWidth     = 36;
    private const int B_HeaderSidePadding   = 26;
    private const int B_FooterHeight        = 30;
    private const int B_FooterSeparatorGap  = 6;
    private const int B_GridBottomGap       = 4;
    private const int B_PadLeft             = 8;
    private const int B_PadTop              = 8;
    private const int B_PadRight            = 14;
    private const int B_PadBottom           = 10;
    private const int B_TodayCircleDiameter = 22;
    private const int B_FooterBoxSize       = 16;

    // ── Live metrics — reset every paint ─────────────────────────────────────
    private int _headerHeight;
    private int _headerVertPadding;
    private int _weekRowHeight;
    private int _cellHeight;
    private int _cellWidth;
    private int _monthGap;
    private int _weekColumnWidth;
    private int _headerSidePadding;
    private int _footerHeight;
    private int _footerSeparatorGap;
    private int _gridBottomGap;
    private int _padLeft, _padTop, _padRight, _padBottom;
    private int _todayCircleDiameter;
    private int _footerBoxSize;

    // ── Theme colors ─────────────────────────────────────────────────────────
    private static readonly Color HeaderBlueColor          = Color.FromArgb(0x2B, 0x74, 0xE6);
    private static readonly Color WeekNumberColor          = Color.FromArgb(0x7A, 0x58, 0x58);
    private static readonly Color TrailingDayColor         = Color.FromArgb(0xB8, 0xB8, 0xBC);
    private static readonly Color TodayCircleColor         = Color.FromArgb(0xB3, 0x1B, 0x1B);
    private static readonly Color DividerColor             = Color.FromArgb(0xE6, 0xE6, 0xE6);
    private static readonly Color WeekHighlightFillColor   = Color.FromArgb(0xE0, 0xEC, 0xFF);
    private static readonly Color WeekHighlightBorderColor = Color.FromArgb(0xAD, 0xCA, 0xFF);

    private readonly float _headerTitleCenterRatio = 0.28f;

    // ── Public configuration ─────────────────────────────────────────────────
    [DefaultValue(typeof(Size), "2, 1")] public Size CalendarDimensions { get; init; } = new(2, 1);
    [DefaultValue(true)]  public bool ShowWeekNumbers  { get; init; } = true;
    [DefaultValue(true)]  public bool ShowToday        { get; init; } = true;
    [DefaultValue(true)]  public bool ShowTodayCircle  { get; init; } = true;

    [DefaultValue(typeof(Color), "Black")]
    public Color HeaderForeground { get; init; } = Color.FromArgb(0x1F, 0x1F, 0x1F);

    [DefaultValue(typeof(Color), "Gray")]
    public Color SecondaryForeground { get; init; } = Color.FromArgb(0x6B, 0x6B, 0x6B);

    [DefaultValue(typeof(Color), "0, 100, 210")]
    public Color Accent { get; init; } = Color.FromArgb(0x1E, 0x6B, 0xD6);

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DateTime DisplayMonth { get; private set; } = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

    // ── Hit rects ────────────────────────────────────────────────────────────
    private Rectangle _prevChevronRect;
    private Rectangle _nextChevronRect;
    private Rectangle _todayBoxRect;
    private Rectangle _footerRect;

    private DateTime? _highlightWeekStart;
    private DateTime? _highlightWeekMonth;

    private readonly CultureInfo _culture;

    public CalendarControl()
    {
        _culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        _culture.DateTimeFormat.CalendarWeekRule = CalendarWeekRule.FirstFourDayWeek;
        _culture.DateTimeFormat.FirstDayOfWeek   = DayOfWeek.Monday;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint  |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw          |
            ControlStyles.UserPaint, true);

        TabStop = false;
        Cursor  = Cursors.Default;

        MouseWheel += (_, e) =>
        {
            DisplayMonth = DisplayMonth.AddMonths(e.Delta > 0 ? -1 : 1);
            Invalidate();
        };
    }

    // ── Public API ───────────────────────────────────────────────────────────
    public void HighlightIsoWeek(DateTime anyDate, DateTime owningMonthFirstOfMonth)
    {
        _highlightWeekStart = StartOfIsoWeek(anyDate);
        _highlightWeekMonth = new DateTime(owningMonthFirstOfMonth.Year, owningMonthFirstOfMonth.Month, 1);
        Invalidate();
    }

    public void HighlightIsoWeek(DateTime anyDate)
    {
        _highlightWeekStart = StartOfIsoWeek(anyDate);
        _highlightWeekMonth = new DateTime(anyDate.Year, anyDate.Month, 1);
        Invalidate();
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        using var g = CreateGraphics();
        ComputeMetrics(g);
        return ComputeTotalSize();
    }

    // ── Paint ─────────────────────────────────────────────────────────────────
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode     = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        ComputeMetrics(g);

        using (var bg = new SolidBrush(BackColor))
            g.FillRectangle(bg, ClientRectangle);

        PaintHeader(g);
        PaintGrid(g);
        PaintFooter(g);
    }

    // ── Header — month titles + chevrons (never slides) ──────────────────────
    private void PaintHeader(Graphics g)
    {
        using var headerFont = new Font(Font.FontFamily, Font.Size + 0.5f, FontStyle.Bold);

        int titleCenterY = _padTop + (int)Math.Round(_headerHeight * _headerTitleCenterRatio);
        int chevronHalfH = (_headerHeight - 10) / 2;
        int chevronW     = _headerSidePadding - 8;

        _prevChevronRect = new Rectangle(
            _padLeft + 1,
            titleCenterY - chevronHalfH,
            chevronW,
            chevronHalfH * 2);

        _nextChevronRect = new Rectangle(
            Width - _padRight - _headerSidePadding + 7,
            titleCenterY - chevronHalfH,
            chevronW,
            chevronHalfH * 2);

        DrawChevron(g, _prevChevronRect, left: true);
        DrawChevron(g, _nextChevronRect, left: false);

        // Draw each month's title inside the fixed header band.
        int across = CalendarDimensions.Width;
        int down   = CalendarDimensions.Height;

        int weekColWidth = ShowWeekNumbers ? _weekColumnWidth : 0;
        int monthWidth   = weekColWidth + _cellWidth * 7;
        int mhFixed      = _headerHeight + _weekRowHeight + _cellHeight * FixedWeeksPerMonth;

        var monthCursor = DisplayMonth;
        int yCursor     = _padTop;
        var dtf         = _culture.DateTimeFormat;

        for (int r = 0; r < down; r++)
        {
            int xCursor = _padLeft;
            for (int c = 0; c < across; c++)
            {
                string title     = $"{dtf.GetMonthName(monthCursor.Month)} {monthCursor.Year}";
                var    titleSize = TextRenderer.MeasureText(g, title, headerFont,
                    new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

                int titleCY  = yCursor + (int)Math.Round(_headerHeight * _headerTitleCenterRatio);
                int titleTop = titleCY - titleSize.Height / 2;

                TextRenderer.DrawText(g, title, headerFont,
                    new Rectangle(xCursor, titleTop, monthWidth, titleSize.Height),
                    HeaderForeground,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top |
                    TextFormatFlags.SingleLine       | TextFormatFlags.NoPadding);

                monthCursor = monthCursor.AddMonths(1);
                xCursor += monthWidth + (c < across - 1 ? _monthGap : 0);
            }
            yCursor += mhFixed + (r < down - 1 ? _monthGap : 0);
        }
    }

    // ── Grid — day-name rows + date cells + between-month dividers (slides) ──
    private void PaintGrid(Graphics g)
    {
        int across  = CalendarDimensions.Width;
        int down    = CalendarDimensions.Height;

        int weekColWidth = ShowWeekNumbers ? _weekColumnWidth : 0;
        int monthWidth   = weekColWidth + _cellWidth * 7;
        int mhFixed      = _headerHeight + _weekRowHeight + _cellHeight * FixedWeeksPerMonth;

        var monthCursor = DisplayMonth;
        int yCursor     = _padTop;

        for (int r = 0; r < down; r++)
        {
            int xCursor = _padLeft;
            for (int c = 0; c < across; c++)
            {
                var bounds = new Rectangle(xCursor, yCursor, monthWidth, mhFixed);
                DrawMonthGrid(g, bounds, monthCursor);
                monthCursor = monthCursor.AddMonths(1);

                if (c < across - 1)
                {
                    int divX = bounds.Right + _monthGap / 2;
                    using var dp = new Pen(DividerColor, 1f);
                    // Divider starts below the header band.
                    g.DrawLine(dp, divX, bounds.Top + _headerHeight, divX, bounds.Bottom);
                }

                xCursor += monthWidth + (c < across - 1 ? _monthGap : 0);
            }
            yCursor += mhFixed + (r < down - 1 ? _monthGap : 0);
        }
    }

    // ── Footer — separator line + today box + today text (never slides) ──────
    private void PaintFooter(Graphics g)
    {
        int down    = CalendarDimensions.Height;
        int mhFixed = _headerHeight + _weekRowHeight + _cellHeight * FixedWeeksPerMonth;
        int sepY    = _padTop + down * mhFixed + (down - 1) * _monthGap + _gridBottomGap;

        using (var sp = new Pen(DividerColor, 1f))
            g.DrawLine(sp, _padLeft, sepY, Width - _padRight, sepY);

        _footerRect = new Rectangle(
            _padLeft,
            sepY + _footerSeparatorGap,
            Width - (_padLeft + _padRight),
            _footerHeight);

        int boxY = _footerRect.Y + (_footerRect.Height - _footerBoxSize) / 2;
        _todayBoxRect = new Rectangle(_footerRect.X, boxY, _footerBoxSize, _footerBoxSize);

        // Fill background behind footer to cover any grid bleed.
        using (var bg = new SolidBrush(BackColor))
            g.FillRectangle(bg, _footerRect);

        using (var boxPen = new Pen(Accent, 2f))
            g.DrawRectangle(boxPen, _todayBoxRect);

        TextRenderer.DrawText(g,
            $"Today: {DateTime.Today.ToString("dd-MM-yyyy", _culture)}",
            Font,
            new Rectangle(_todayBoxRect.Right + 8, _footerRect.Y,
                          _footerRect.Width - (_footerBoxSize + 8), _footerRect.Height),
            SecondaryForeground,
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.Left           |
            TextFormatFlags.SingleLine     |
            TextFormatFlags.NoPadding);
    }

    // ── Month grid — day-name row + date cells for one month ─────────────────
    private void DrawMonthGrid(Graphics g, Rectangle bounds, DateTime month)
    {
        var dtf          = _culture.DateTimeFormat;
        var todayCircles = new List<Rectangle>();

        int weekColWidth = ShowWeekNumbers ? _weekColumnWidth : 0;
        int colX         = bounds.X;
        int namesY       = bounds.Y + _headerHeight;

        // Day-name header row (CW + Mon..Sun).
        if (ShowWeekNumbers)
        {
            TextRenderer.DrawText(g, "CW", Font,
                new Rectangle(colX, namesY, weekColWidth, _weekRowHeight),
                Color.FromArgb(0xB0, 0xB0, 0xB0),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine       | TextFormatFlags.NoPadding);
            colX += weekColWidth;
        }

        for (int d = 0; d < 7; d++)
        {
            string name = dtf.AbbreviatedDayNames[((int)dtf.FirstDayOfWeek + d) % 7];
            TextRenderer.DrawText(g, name, Font,
                new Rectangle(colX, namesY, _cellWidth, _weekRowHeight),
                HeaderBlueColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine       | TextFormatFlags.NoPadding);
            colX += _cellWidth;
        }

        // Date grid.
        DateTime firstOfMonth = new DateTime(month.Year, month.Month, 1);
        int      offset       = (((int)firstOfMonth.DayOfWeek - (int)dtf.FirstDayOfWeek) + 7) % 7;
        DateTime gridStart    = firstOfMonth.AddDays(-offset);

        int gridXStart = bounds.X + weekColWidth;
        int gridY      = bounds.Y + _headerHeight + _weekRowHeight;

        for (int row = 0; row < FixedWeeksPerMonth; row++)
        {
            DateTime rowWeekStart = StartOfIsoWeek(gridStart.AddDays(row * 7));

            if (_highlightWeekStart.HasValue &&
                _highlightWeekMonth.HasValue &&
                rowWeekStart == _highlightWeekStart.Value &&
                month.Date   == _highlightWeekMonth.Value)
            {
                var bar = Rectangle.FromLTRB(
                    bounds.X + 2, gridY,
                    bounds.Right - 2, gridY + _cellHeight);

                using var fill = new SolidBrush(WeekHighlightFillColor);
                using var hpen = new Pen(WeekHighlightBorderColor, 1f);
                g.FillRectangle(fill, bar);
                g.DrawRectangle(hpen, bar.X, bar.Y, bar.Width - 1, bar.Height - 1);
            }

            if (ShowWeekNumbers)
            {
                int weekNumber = ISOWeek.GetWeekOfYear(rowWeekStart);
                TextRenderer.DrawText(g, weekNumber.ToString(), Font,
                    new Rectangle(bounds.X, gridY, weekColWidth, _cellHeight),
                    WeekNumberColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine       | TextFormatFlags.NoPadding);
            }

            int cx = gridXStart;
            for (int col = 0; col < 7; col++)
            {
                var  day     = gridStart.AddDays(row * 7 + col);
                var  rect    = new Rectangle(cx, gridY, _cellWidth, _cellHeight);
                bool inMonth = day.Month == month.Month;
                bool isToday = day.Date  == DateTime.Today.Date;

                TextRenderer.DrawText(g, day.Day.ToString(), Font, rect,
                    inMonth ? HeaderForeground : TrailingDayColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine       | TextFormatFlags.NoPadding);

                if (ShowToday && ShowTodayCircle && isToday && inMonth)
                {
                    todayCircles.Add(new Rectangle(
                        rect.X + (rect.Width  - _todayCircleDiameter) / 2,
                        rect.Y + (rect.Height - _todayCircleDiameter) / 2,
                        _todayCircleDiameter,
                        _todayCircleDiameter));
                }

                cx += _cellWidth;
            }

            gridY += _cellHeight;
        }

        using var todayPen = new Pen(TodayCircleColor, 1.8f);
        foreach (var circle in todayCircles)
            g.DrawEllipse(todayPen, circle);
    }

    // ── Chevron ───────────────────────────────────────────────────────────────
    private void DrawChevron(Graphics g, Rectangle rect, bool left)
    {
        using var p = new Pen(Color.FromArgb(0x3D, 0x3D, 0x3D), 2f);
        int cx   = rect.X + rect.Width  / 2;
        int cy   = rect.Y + rect.Height / 2;
        int size = Math.Max(6, rect.Height / 2);

        g.DrawLines(p,
            left
                ? new[] { new Point(cx + 3, cy - size), new Point(cx - 4, cy), new Point(cx + 3, cy + size) }
                : new[] { new Point(cx - 3, cy - size), new Point(cx + 4, cy), new Point(cx - 3, cy + size) });
    }

    // ── Metrics ───────────────────────────────────────────────────────────────
    private void ComputeMetrics(Graphics g)
    {
        _headerVertPadding   = B_HeaderVertPadding;
        _weekRowHeight       = B_WeekRowHeight;
        _cellHeight          = B_CellHeight;
        _cellWidth           = B_CellWidth;
        _monthGap            = B_MonthGap;
        _weekColumnWidth     = B_WeekColumnWidth;
        _headerSidePadding   = B_HeaderSidePadding;
        _footerHeight        = B_FooterHeight;
        _footerSeparatorGap  = B_FooterSeparatorGap;
        _gridBottomGap       = B_GridBottomGap;
        _padLeft             = B_PadLeft;
        _padTop              = B_PadTop;
        _padRight            = B_PadRight;
        _padBottom           = B_PadBottom;
        _todayCircleDiameter = B_TodayCircleDiameter;
        _footerBoxSize       = B_FooterBoxSize;

        var dtf = _culture.DateTimeFormat;

        string longestDay = "Wed";
        foreach (var abbr in dtf.AbbreviatedDayNames)
            if (abbr.Length > longestDay.Length) longestDay = abbr;

        var daySize = TextRenderer.MeasureText(g, longestDay, Font,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        var weekSize = TextRenderer.MeasureText(g, "53", Font,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        using var hf = new Font(Font, FontStyle.Bold);
        var titleSize = TextRenderer.MeasureText(g, "September 2025", hf,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        _cellWidth       = Math.Max(_cellWidth,       daySize.Width  + 8);
        _weekColumnWidth = Math.Max(_weekColumnWidth,  weekSize.Width + 10);
        _weekRowHeight   = Math.Max(_weekRowHeight,    daySize.Height + 4);
        _cellHeight      = Math.Max(_cellHeight,       daySize.Height + 8);

        _todayCircleDiameter = Math.Min(_todayCircleDiameter,
            Math.Min(_cellWidth, _cellHeight) - 2);

        _headerHeight = titleSize.Height + 2 * _headerVertPadding;
    }

    private Size ComputeTotalSize()
    {
        int across = CalendarDimensions.Width;
        int down   = CalendarDimensions.Height;

        int weekColWidth = ShowWeekNumbers ? _weekColumnWidth : 0;
        int monthWidth   = weekColWidth + _cellWidth * 7;
        int mhFixed      = _headerHeight + _weekRowHeight + _cellHeight * FixedWeeksPerMonth;

        int totalWidth  = _padLeft + across * monthWidth + (across - 1) * _monthGap + _padRight;
        int totalHeight = _padTop  + down * mhFixed + (down - 1) * _monthGap
                        + _gridBottomGap + 1 + _footerSeparatorGap + _footerHeight + _padBottom;

        return new Size(totalWidth, totalHeight);
    }

    // ── Mouse ─────────────────────────────────────────────────────────────────
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (_prevChevronRect.Contains(e.Location)) { DisplayMonth = DisplayMonth.AddMonths(-1); Invalidate(); return; }
        if (_nextChevronRect.Contains(e.Location)) { DisplayMonth = DisplayMonth.AddMonths(+1); Invalidate(); return; }

        if (_footerRect.Contains(e.Location))
        {
            var today = DateTime.Today;
            DisplayMonth = new DateTime(today.Year, today.Month, 1);
            HighlightIsoWeek(today, new DateTime(today.Year, today.Month, 1));
            Invalidate();
            return;
        }

        int across = CalendarDimensions.Width;
        int down   = CalendarDimensions.Height;

        int weekColWidth = ShowWeekNumbers ? _weekColumnWidth : 0;
        int monthWidth   = weekColWidth + _cellWidth * 7;
        int mhFixed      = _headerHeight + _weekRowHeight + _cellHeight * FixedWeeksPerMonth;

        var monthCursor = DisplayMonth;
        int yCursor     = _padTop;

        for (int r = 0; r < down; r++)
        {
            int xCursor = _padLeft;
            for (int c = 0; c < across; c++)
            {
                var bounds = new Rectangle(xCursor, yCursor, monthWidth, mhFixed);

                if (bounds.Contains(e.Location))
                {
                    int gridTop = bounds.Y + _headerHeight + _weekRowHeight;
                    int yRel    = e.Y - gridTop;

                    if (yRel >= 0 && yRel < _cellHeight * FixedWeeksPerMonth)
                    {
                        int row = yRel / _cellHeight;
                        var dtf = _culture.DateTimeFormat;

                        var firstOfMonth = new DateTime(monthCursor.Year, monthCursor.Month, 1);
                        int offset       = ((int)firstOfMonth.DayOfWeek - (int)dtf.FirstDayOfWeek + 7) % 7;
                        var gridStart    = firstOfMonth.AddDays(-offset);

                        _highlightWeekStart = StartOfIsoWeek(gridStart.AddDays(row * 7));
                        _highlightWeekMonth = firstOfMonth;
                        Invalidate();
                        return;
                    }
                }

                xCursor += monthWidth + (c < across - 1 ? _monthGap : 0);
                monthCursor = monthCursor.AddMonths(1);
            }
            yCursor += mhFixed + (r < down - 1 ? _monthGap : 0);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static DateTime StartOfIsoWeek(DateTime date)
    {
        int dow = (int)date.DayOfWeek;
        if (dow == 0) dow = 7;
        return date.AddDays(1 - dow).Date;
    }
}