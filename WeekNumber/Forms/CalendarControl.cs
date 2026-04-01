using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace WeekNumber.Forms;

/// <summary>
/// A DPI-independent calendar control with ISO 8601 week numbers,
/// modern light styling, hover effects, and rounded visual elements.
/// </summary>
internal sealed class CalendarControl : Control
{
    private const int FixedWeeksPerMonth = 6;

    // ── Base metrics at 96 DPI — scaled linearly via DeviceDpi ──────────────

    private const int B_HeaderVertPad  = 10;
    private const int B_WeekRowHeight  = 22;
    private const int B_CellHeight     = 26;
    private const int B_CellWidth      = 36;
    private const int B_MonthGap       = 16;
    private const int B_WeekColWidth   = 38;
    private const int B_HeaderSidePad  = 28;
    private const int B_FooterHeight   = 30;
    private const int B_FooterSepGap   = 8;
    private const int B_GridBottomGap  = 4;
    private const int B_PadLeft        = 8;
    private const int B_PadTop         = 8;
    private const int B_PadRight       = 14;
    private const int B_PadBottom      = 8;
    private const int B_TodayDiameter  = 24;
    private const int B_FooterDotSize  = 8;
    private const int B_CornerRadius   = 10;

    // ── Scaled metrics (recomputed every paint) ─────────────────────────────

    private int _headerHeight;
    private int _headerVertPad;
    private int _weekRowHeight;
    private int _cellHeight;
    private int _cellWidth;
    private int _monthGap;
    private int _weekColWidth;
    private int _headerSidePad;
    private int _footerHeight;
    private int _footerSepGap;
    private int _gridBottomGap;
    private int _padLeft, _padTop, _padRight, _padBottom;
    private int _todayDiameter;
    private int _footerDotSize;
    private int _cornerRadius;

    // ── Fixed colors (light theme) ──────────────────────────────────────────

    private static readonly Color TitleTextColor       = Color.FromArgb(0x1A, 0x1A, 0x1A);
    private static readonly Color YearTextColor        = Color.FromArgb(0x70, 0x70, 0x70);
    private static readonly Color DayNameTextColor     = Color.FromArgb(0x2B, 0x74, 0xE6);
    private static readonly Color TrailingTextColor    = Color.FromArgb(0xC0, 0xC0, 0xC4);
    private static readonly Color WeekNumberTextColor  = Color.FromArgb(0x7A, 0x58, 0x58);
    private static readonly Color CwLabelTextColor     = Color.FromArgb(0x6B, 0x7B, 0x8D);
    private static readonly Color TodayTextColor       = Color.White;
    private static readonly Color DividerColor         = Color.FromArgb(0xE8, 0xE8, 0xE8);
    private static readonly Color HighlightFillColor   = Color.FromArgb(0x18, 0x40, 0x80, 0xE0);
    private static readonly Color HoverFillColor       = Color.FromArgb(0x3C, 0x00, 0x5F, 0xB8);
    private static readonly Color ChevronNormalColor   = Color.FromArgb(0x70, 0x70, 0x70);
    private static readonly Color ChevronHoverColor    = Color.FromArgb(0x2B, 0x74, 0xE6);

    // ── Font cache ──────────────────────────────────────────────────────────

    private Font? _titleFont;
    private Font? _yearFont;
    private Font? _dayNameFont;
    private Font? _bodyFont;
    private Font? _weekNumFont;
    private string _cachedFontKey = "";

    // ── Hover state ─────────────────────────────────────────────────────────

    private DateTime? _hoveredDate;
    private DateTime? _hoveredMonthStart;
    private bool _chevronLeftHovered;
    private bool _chevronRightHovered;
    private bool _footerHovered;

    // ── Hit rects ───────────────────────────────────────────────────────────

    private Rectangle _prevChevronRect;
    private Rectangle _nextChevronRect;
    private Rectangle _footerRect;

    // ── Highlight state ─────────────────────────────────────────────────────

    private DateTime? _highlightWeekStart;
    private DateTime? _highlightWeekMonth;

    private readonly CultureInfo _culture;

    // ── Public configuration ────────────────────────────────────────────────

    [DefaultValue(typeof(Size), "2, 1")]
    public Size CalendarDimensions { get; init; } = new(2, 1);

    [DefaultValue(true)] public bool ShowWeekNumbers { get; init; } = true;
    [DefaultValue(true)] public bool ShowToday       { get; init; } = true;
    [DefaultValue(true)] public bool ShowTodayCircle { get; init; } = true;

    [DefaultValue(typeof(Color), "Black")]
    public Color HeaderForeground { get; init; } = Color.FromArgb(0x1F, 0x1F, 0x1F);

    [DefaultValue(typeof(Color), "Gray")]
    public Color SecondaryForeground { get; init; } = Color.FromArgb(0x6B, 0x6B, 0x6B);

    [DefaultValue(typeof(Color), "0, 100, 210")]
    public Color Accent { get; init; } = Color.FromArgb(0x1E, 0x6B, 0xD6);

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DateTime DisplayMonth { get; private set; } =
        new(DateTime.Today.Year, DateTime.Today.Month, 1);

    // ═════════════════════════════════════════════════════════════════════════
    //  Constructor
    // ═════════════════════════════════════════════════════════════════════════

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

    // ═════════════════════════════════════════════════════════════════════════
    //  Public API
    // ═════════════════════════════════════════════════════════════════════════

    public void HighlightIsoWeek(DateTime anyDate, DateTime owningMonthFirstOfMonth)
    {
        _highlightWeekStart = StartOfIsoWeek(anyDate);
        _highlightWeekMonth = new DateTime(owningMonthFirstOfMonth.Year,
                                           owningMonthFirstOfMonth.Month, 1);
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

    // ═════════════════════════════════════════════════════════════════════════
    //  Paint
    // ═════════════════════════════════════════════════════════════════════════

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode     = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode   = PixelOffsetMode.HighQuality;

        ComputeMetrics(g);
        EnsureFonts();

        // Use parent's background to blend seamlessly with the host form
        var bgColor = Parent?.BackColor ?? BackColor;
        using (var bg = new SolidBrush(bgColor))
            g.FillRectangle(bg, ClientRectangle);

        PaintHeader(g);
        PaintGrid(g);
        PaintFooter(g);
    }

    // ── Header ──────────────────────────────────────────────────────────────

    private void PaintHeader(Graphics g)
    {
        int across     = CalendarDimensions.Width;
        int down       = CalendarDimensions.Height;
        int weekCol    = ShowWeekNumbers ? _weekColWidth : 0;
        int monthWidth = weekCol + _cellWidth * 7;
        int mhFixed    = _headerHeight + _weekRowHeight + _cellHeight * FixedWeeksPerMonth;

        int titleCenterY = _padTop + _headerHeight / 2;

        // ── Chevrons ──

        int chevronW = _headerSidePad - 8;
        int chevronH = _headerHeight - 16;

        _prevChevronRect = new Rectangle(
            _padLeft, titleCenterY - chevronH / 2,
            chevronW + 8, chevronH);

        int totalWidth = across * monthWidth + (across - 1) * _monthGap;
        _nextChevronRect = new Rectangle(
            _padLeft + totalWidth - chevronW - 8,
            titleCenterY - chevronH / 2,
            chevronW + 8, chevronH);

        DrawChevron(g, _prevChevronRect, left: true);
        DrawChevron(g, _nextChevronRect, left: false);

        // ── Month / year titles ──

        var monthCursor = DisplayMonth;
        int yCursor     = _padTop;

        for (int r = 0; r < down; r++)
        {
            int xCursor = _padLeft;
            for (int c = 0; c < across; c++)
            {
                DrawMonthTitle(g, monthCursor, xCursor, yCursor, monthWidth);
                monthCursor  = monthCursor.AddMonths(1);
                xCursor     += monthWidth + (c < across - 1 ? _monthGap : 0);
            }
            yCursor += mhFixed + (r < down - 1 ? _monthGap : 0);
        }
    }

    private void DrawMonthTitle(Graphics g, DateTime month, int x, int y, int monthWidth)
    {
        var dtf = _culture.DateTimeFormat;

        string monthName = dtf.GetMonthName(month.Month);
        string yearStr   = $" {month.Year}";

        var monthSize = TextRenderer.MeasureText(g, monthName, _titleFont!,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        var yearSize = TextRenderer.MeasureText(g, yearStr, _yearFont!,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        int totalW   = monthSize.Width + yearSize.Width;
        int startX   = x + (monthWidth - totalW) / 2;
        int centerY  = y + _headerHeight / 2;
        int monthTop = centerY - monthSize.Height / 2;
        int yearTop  = centerY - yearSize.Height / 2;

        TextRenderer.DrawText(g, monthName, _titleFont!,
            new Point(startX, monthTop), TitleTextColor,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        TextRenderer.DrawText(g, yearStr, _yearFont!,
            new Point(startX + monthSize.Width, yearTop), YearTextColor,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
    }

    // ── Grid ────────────────────────────────────────────────────────────────

    private void PaintGrid(Graphics g)
    {
        int across     = CalendarDimensions.Width;
        int down       = CalendarDimensions.Height;
        int weekCol    = ShowWeekNumbers ? _weekColWidth : 0;
        int monthWidth = weekCol + _cellWidth * 7;
        int mhFixed    = _headerHeight + _weekRowHeight + _cellHeight * FixedWeeksPerMonth;

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
                    int divX   = bounds.Right + _monthGap / 2;
                    int divTop = bounds.Y + _headerHeight + 4;
                    int divBot = bounds.Bottom - 4;
                    using var dp = new Pen(DividerColor, 1f);
                    g.DrawLine(dp, divX, divTop, divX, divBot);
                }

                xCursor += monthWidth + (c < across - 1 ? _monthGap : 0);
            }
            yCursor += mhFixed + (r < down - 1 ? _monthGap : 0);
        }
    }

    private void DrawMonthGrid(Graphics g, Rectangle bounds, DateTime month)
    {
        var dtf     = _culture.DateTimeFormat;
        int weekCol = ShowWeekNumbers ? _weekColWidth : 0;
        int colX    = bounds.X;
        int namesY  = bounds.Y + _headerHeight;

        if (ShowWeekNumbers)
        {
            TextRenderer.DrawText(g, "CW", _dayNameFont!,
                new Rectangle(colX, namesY, weekCol, _weekRowHeight),
                CwLabelTextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine       | TextFormatFlags.NoPadding);
            colX += weekCol;
        }

        for (int d = 0; d < 7; d++)
        {
            string name = dtf.AbbreviatedDayNames[((int)dtf.FirstDayOfWeek + d) % 7];
            TextRenderer.DrawText(g, name, _dayNameFont!,
                new Rectangle(colX, namesY, _cellWidth, _weekRowHeight),
                DayNameTextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine       | TextFormatFlags.NoPadding);
            colX += _cellWidth;
        }

        var firstOfMonth = new DateTime(month.Year, month.Month, 1);
        int offset       = (((int)firstOfMonth.DayOfWeek - (int)dtf.FirstDayOfWeek) + 7) % 7;
        var gridStart    = firstOfMonth.AddDays(-offset);

        int gridXStart = bounds.X + weekCol;
        int gridY      = bounds.Y + _headerHeight + _weekRowHeight;

        for (int row = 0; row < FixedWeeksPerMonth; row++)
        {
            DateTime rowWeekStart = StartOfIsoWeek(gridStart.AddDays(row * 7));

            bool isHighlighted =
                _highlightWeekStart.HasValue &&
                _highlightWeekMonth.HasValue &&
                rowWeekStart   == _highlightWeekStart.Value &&
                month.Date     == _highlightWeekMonth.Value;

            if (isHighlighted)
            {
                var barRect = new RectangleF(
                    bounds.X + 2f, gridY + 1f,
                    bounds.Width - 4f, _cellHeight - 2f);

                using var path = CreateRoundedRect(barRect, _cornerRadius);
                using var fill = new SolidBrush(HighlightFillColor);
                g.FillPath(fill, path);
            }

            if (ShowWeekNumbers)
            {
                int weekNumber = ISOWeek.GetWeekOfYear(rowWeekStart);
                TextRenderer.DrawText(g, weekNumber.ToString(), _weekNumFont!,
                    new Rectangle(bounds.X, gridY, weekCol, _cellHeight),
                    WeekNumberTextColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine       | TextFormatFlags.NoPadding);
            }

            int cx = gridXStart;
            for (int col = 0; col < 7; col++)
            {
                var  day     = gridStart.AddDays(row * 7 + col);
                var  rect    = new Rectangle(cx, gridY, _cellWidth, _cellHeight);
                bool inMonth = day.Month == month.Month;
                bool isToday = day.Date  == DateTime.Today;

                bool isHovered =
                    _hoveredDate.HasValue       &&
                    _hoveredMonthStart.HasValue  &&
                    day.Date   == _hoveredDate.Value.Date &&
                    month.Date == _hoveredMonthStart.Value.Date;

                int diam = _todayDiameter;
                var circleRect = new RectangleF(
                    rect.X + (rect.Width  - diam) / 2f,
                    rect.Y + (rect.Height - diam) / 2f,
                    diam, diam);

                if (isHovered && !(ShowToday && ShowTodayCircle && isToday && inMonth))
                {
                    using var hBrush = new SolidBrush(HoverFillColor);
                    g.FillEllipse(hBrush, circleRect);
                }

                if (ShowToday && ShowTodayCircle && isToday && inMonth)
                {
                    using var todayBrush = new SolidBrush(Accent);
                    g.FillEllipse(todayBrush, circleRect);
                }

                Color textColor;
                if (ShowToday && ShowTodayCircle && isToday && inMonth)
                    textColor = TodayTextColor;
                else if (inMonth)
                    textColor = HeaderForeground;
                else
                    textColor = TrailingTextColor;

                TextRenderer.DrawText(g, day.Day.ToString(), _bodyFont!, rect,
                    textColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine       | TextFormatFlags.NoPadding);

                cx += _cellWidth;
            }

            gridY += _cellHeight;
        }
    }

    // ── Footer ──────────────────────────────────────────────────────────────

    private void PaintFooter(Graphics g)
    {
        int down    = CalendarDimensions.Height;
        int weekCol = ShowWeekNumbers ? _weekColWidth : 0;
        int mhFixed = _headerHeight + _weekRowHeight + _cellHeight * FixedWeeksPerMonth;

        int totalWidth = CalendarDimensions.Width * (weekCol + _cellWidth * 7)
                       + (CalendarDimensions.Width - 1) * _monthGap;

        int sepY = _padTop + down * mhFixed + (down - 1) * _monthGap + _gridBottomGap;

        using (var sp = new Pen(DividerColor, 1f))
            g.DrawLine(sp, _padLeft, sepY, _padLeft + totalWidth, sepY);

        _footerRect = new Rectangle(
            _padLeft, sepY + _footerSepGap,
            totalWidth, _footerHeight);

        int dotY = _footerRect.Y + (_footerRect.Height - _footerDotSize) / 2;
        var dotRect = new Rectangle(_footerRect.X + 4, dotY, _footerDotSize, _footerDotSize);
        using (var dotBrush = new SolidBrush(Accent))
            g.FillEllipse(dotBrush, dotRect);

        string todayLabel = $"Today: {DateTime.Today:dd-MM-yyyy}";

        TextRenderer.DrawText(g, todayLabel, _dayNameFont!,
            new Rectangle(dotRect.Right + 8, _footerRect.Y,
                          _footerRect.Width - dotRect.Right - 8 + _footerRect.X,
                          _footerRect.Height),
            _footerHovered ? Accent : SecondaryForeground,
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.Left           |
            TextFormatFlags.SingleLine     |
            TextFormatFlags.NoPadding);
    }

    // ── Chevron ─────────────────────────────────────────────────────────────

    private void DrawChevron(Graphics g, Rectangle rect, bool left)
    {
        bool hovered = left ? _chevronLeftHovered : _chevronRightHovered;

        if (hovered)
        {
            int cx = rect.X + rect.Width  / 2;
            int cy = rect.Y + rect.Height / 2;
            int r  = Math.Min(rect.Width, rect.Height) / 2 + 2;
            using var hBrush = new SolidBrush(HoverFillColor);
            g.FillEllipse(hBrush, cx - r, cy - r, r * 2, r * 2);
        }

        var color = hovered ? ChevronHoverColor : ChevronNormalColor;
        using var pen = new Pen(color, 1.6f)
        {
            LineJoin  = LineJoin.Round,
            StartCap  = LineCap.Round,
            EndCap    = LineCap.Round
        };

        int midX = rect.X + rect.Width  / 2;
        int midY = rect.Y + rect.Height / 2;
        int half = Math.Max(5, rect.Height / 3);

        g.DrawLines(pen,
            left
                ? new[] { new Point(midX + 2, midY - half), new Point(midX - 3, midY), new Point(midX + 2, midY + half) }
                : new[] { new Point(midX - 2, midY - half), new Point(midX + 3, midY), new Point(midX - 2, midY + half) });
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Mouse interaction
    // ═════════════════════════════════════════════════════════════════════════

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool dirty = false;

        bool newLeft  = _prevChevronRect.Contains(e.Location);
        bool newRight = _nextChevronRect.Contains(e.Location);
        if (newLeft != _chevronLeftHovered || newRight != _chevronRightHovered)
        {
            _chevronLeftHovered  = newLeft;
            _chevronRightHovered = newRight;
            dirty = true;
        }

        bool newFooter = _footerRect.Contains(e.Location);
        if (newFooter != _footerHovered)
        {
            _footerHovered = newFooter;
            dirty = true;
        }

        Cursor = (newLeft || newRight || newFooter) ? Cursors.Hand : Cursors.Default;

        var (hitDate, hitMonth) = HitTestCell(e.Location);
        if (hitDate != _hoveredDate || hitMonth != _hoveredMonthStart)
        {
            _hoveredDate       = hitDate;
            _hoveredMonthStart = hitMonth;
            dirty = true;
        }

        if (dirty) Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        bool dirty = _chevronLeftHovered || _chevronRightHovered ||
                     _footerHovered      || _hoveredDate.HasValue;

        _chevronLeftHovered  = false;
        _chevronRightHovered = false;
        _footerHovered       = false;
        _hoveredDate         = null;
        _hoveredMonthStart   = null;
        Cursor               = Cursors.Default;

        if (dirty) Invalidate();
    }

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
            DisplayMonth = DisplayMonth.AddMonths(+1);
            Invalidate();
            return;
        }

        if (_footerRect.Contains(e.Location))
        {
            var today = DateTime.Today;
            DisplayMonth = new DateTime(today.Year, today.Month, 1);
            HighlightIsoWeek(today, DisplayMonth);
            Invalidate();
            return;
        }

        var (hitDate, hitMonth) = HitTestCell(e.Location);
        if (hitDate.HasValue && hitMonth.HasValue)
        {
            _highlightWeekStart = StartOfIsoWeek(hitDate.Value);
            _highlightWeekMonth = hitMonth.Value;
            Invalidate();
        }
    }

    // ── Hit testing ─────────────────────────────────────────────────────────

    private (DateTime? date, DateTime? monthStart) HitTestCell(Point pt)
    {
        int across     = CalendarDimensions.Width;
        int down       = CalendarDimensions.Height;
        int weekCol    = ShowWeekNumbers ? _weekColWidth : 0;
        int monthWidth = weekCol + _cellWidth * 7;
        int mhFixed    = _headerHeight + _weekRowHeight + _cellHeight * FixedWeeksPerMonth;

        var monthCursor = DisplayMonth;
        int yCursor     = _padTop;

        for (int r = 0; r < down; r++)
        {
            int xCursor = _padLeft;
            for (int c = 0; c < across; c++)
            {
                var bounds = new Rectangle(xCursor, yCursor, monthWidth, mhFixed);

                if (bounds.Contains(pt))
                {
                    int gridTop = bounds.Y + _headerHeight + _weekRowHeight;
                    int yRel    = pt.Y - gridTop;
                    int xRel    = pt.X - (bounds.X + weekCol);

                    if (yRel >= 0 && yRel < _cellHeight * FixedWeeksPerMonth &&
                        xRel >= 0 && xRel < _cellWidth * 7)
                    {
                        int row = yRel / _cellHeight;
                        int col = xRel / _cellWidth;

                        var dtf          = _culture.DateTimeFormat;
                        var firstOfMonth = new DateTime(monthCursor.Year, monthCursor.Month, 1);
                        int off          = ((int)firstOfMonth.DayOfWeek - (int)dtf.FirstDayOfWeek + 7) % 7;
                        var gridStart    = firstOfMonth.AddDays(-off);
                        var day          = gridStart.AddDays(row * 7 + col);

                        return (day, firstOfMonth);
                    }

                    return (null, null);
                }

                xCursor    += monthWidth + (c < across - 1 ? _monthGap : 0);
                monthCursor = monthCursor.AddMonths(1);
            }
            yCursor += mhFixed + (r < down - 1 ? _monthGap : 0);
        }

        return (null, null);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  DPI + font lifecycle
    // ═════════════════════════════════════════════════════════════════════════

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        DisposeFonts();
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        DisposeFonts();
        Invalidate();
    }

    private void EnsureFonts()
    {
        string key = $"{Font.FontFamily.Name}|{Font.Size:F2}|{DeviceDpi}";
        if (_titleFont != null && _cachedFontKey == key) return;

        DisposeFonts();
        _cachedFontKey = key;

        float basePt = Font.Size;
        var family   = Font.FontFamily;

        _titleFont   = new Font(family, basePt + 1f,   FontStyle.Bold);
        _yearFont    = new Font(family, basePt + 1f,   FontStyle.Regular);
        _dayNameFont = new Font(family, basePt - 0.5f, FontStyle.Regular);
        _bodyFont    = new Font(family, basePt,         FontStyle.Regular);
        _weekNumFont = new Font(family, basePt - 0.5f, FontStyle.Regular);
    }

    private void DisposeFonts()
    {
        _titleFont?.Dispose();   _titleFont   = null;
        _yearFont?.Dispose();    _yearFont    = null;
        _dayNameFont?.Dispose(); _dayNameFont = null;
        _bodyFont?.Dispose();    _bodyFont    = null;
        _weekNumFont?.Dispose(); _weekNumFont = null;
        _cachedFontKey = "";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) DisposeFonts();
        base.Dispose(disposing);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Metrics
    // ═════════════════════════════════════════════════════════════════════════

    private void ComputeMetrics(Graphics g)
    {
        float scale = DeviceDpi / 96f;

        _headerVertPad = Scale(B_HeaderVertPad, scale);
        _weekRowHeight = Scale(B_WeekRowHeight, scale);
        _cellHeight    = Scale(B_CellHeight,    scale);
        _cellWidth     = Scale(B_CellWidth,     scale);
        _monthGap      = Scale(B_MonthGap,      scale);
        _weekColWidth  = Scale(B_WeekColWidth,  scale);
        _headerSidePad = Scale(B_HeaderSidePad, scale);
        _footerHeight  = Scale(B_FooterHeight,  scale);
        _footerSepGap  = Scale(B_FooterSepGap,  scale);
        _gridBottomGap = Scale(B_GridBottomGap, scale);
        _padLeft       = Scale(B_PadLeft,       scale);
        _padTop        = Scale(B_PadTop,        scale);
        _padRight      = Scale(B_PadRight,      scale);
        _padBottom     = Scale(B_PadBottom,      scale);
        _todayDiameter = Scale(B_TodayDiameter, scale);
        _footerDotSize = Scale(B_FooterDotSize, scale);
        _cornerRadius  = Scale(B_CornerRadius,  scale);

        EnsureFonts();

        var dtf = _culture.DateTimeFormat;

        string longestDay = "Wed";
        foreach (var abbr in dtf.AbbreviatedDayNames)
            if (abbr.Length > longestDay.Length) longestDay = abbr;

        var daySize = TextRenderer.MeasureText(g, longestDay, _dayNameFont!,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        var weekSize = TextRenderer.MeasureText(g, "53", _weekNumFont!,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        var titleSize = TextRenderer.MeasureText(g, "September 2025", _titleFont!,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        _cellWidth     = Math.Max(_cellWidth,     daySize.Width  + Scale(8,  scale));
        _weekColWidth  = Math.Max(_weekColWidth,  weekSize.Width + Scale(10, scale));
        _weekRowHeight = Math.Max(_weekRowHeight,  daySize.Height + Scale(4,  scale));
        _cellHeight    = Math.Max(_cellHeight,     daySize.Height + Scale(8,  scale));

        _todayDiameter = Math.Min(_todayDiameter,
            Math.Min(_cellWidth, _cellHeight) - 4);

        _headerHeight  = titleSize.Height + 2 * _headerVertPad;
    }

    private Size ComputeTotalSize()
    {
        int across     = CalendarDimensions.Width;
        int down       = CalendarDimensions.Height;
        int weekCol    = ShowWeekNumbers ? _weekColWidth : 0;
        int monthWidth = weekCol + _cellWidth * 7;
        int mhFixed    = _headerHeight + _weekRowHeight + _cellHeight * FixedWeeksPerMonth;

        int totalWidth  = _padLeft + across * monthWidth + (across - 1) * _monthGap + _padRight;
        int totalHeight = _padTop  + down * mhFixed + (down - 1) * _monthGap
                        + _gridBottomGap + 1 + _footerSepGap + _footerHeight + _padBottom;

        return new Size(totalWidth, totalHeight);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════════════════

    private static GraphicsPath CreateRoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0.5f)
        {
            path.AddRectangle(rect);
            return path;
        }

        float d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static DateTime StartOfIsoWeek(DateTime date)
    {
        int dow = (int)date.DayOfWeek;
        if (dow == 0) dow = 7;
        return date.AddDays(1 - dow).Date;
    }

    private static int Scale(int value, float scale) => (int)Math.Round(value * scale);
}