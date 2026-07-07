"""Calendar popup UI for WeekNumber."""

import datetime as dt
import tkinter as tk
import tkinter.font as tkfont
from typing import TYPE_CHECKING

try:
    from PIL import Image, ImageChops, ImageDraw, ImageOps
except ImportError:
    import sys
    print("Missing dependency: Pillow\nInstall with: pip install pillow")
    sys.exit(1)

try:
    # Optional: drives the rounded state pills and the column-width spacer
    # (see CalendarPopup._build_cell_images). Missing on the rare Pillow build
    # without Tk support; the grid then falls back to flat fills at a fixed
    # character width.
    from PIL import ImageTk
except Exception:
    ImageTk = None

from weeknumber_core import (
    POINT,
    RECT,
    SettingsStore,
    cursor_monitor_geometry,
    holiday_name,
    log,
    set_rounded_corners,
    theme_colors,
    toplevel_hwnd,
    user32,
)
from weeknumber_clipboard import format_calendar_week, format_gui_date, format_iso_week

if TYPE_CHECKING:
    # ImageTk is imported above with a None fallback, so it is a variable to
    # static analyzers and cannot sit in a type expression. Pull the
    # PhotoImage class in here for annotations only.
    from PIL.ImageTk import PhotoImage as TkPhotoImage


#: Resolved UI font family, cached process-wide (the installed font set does
#: not change at runtime). "Segoe UI Variable Text" is the Win11 body-optical
#: face; plain "Segoe UI" is the Win10 fallback. Never hard-code the Variable
#: name without probing: on systems lacking it Tk silently substitutes an
#: arbitrary default, which looks worse than Segoe UI.
_FONT_FAMILY: "str | None" = None


def ui_font_family(widget: tk.Misc) -> str:
    global _FONT_FAMILY
    if _FONT_FAMILY is None:
        try:
            families = set(tkfont.families(widget))
        except tk.TclError:
            families = set()
        _FONT_FAMILY = next(
            (f for f in ("Segoe UI Variable Text", "Segoe UI Variable", "Segoe UI")
             if f in families),
            "Segoe UI")
    return _FONT_FAMILY


class ThemedPopup:
    """Shared palette/metric helpers for WeekNumber's Tk windows (the
    calendar flyout, the About window). Subclasses set ``self.scale`` (the
    target monitor's effective-DPI scale factor), ``self.theme`` (a palette
    snapshot from theme_colors) and ``self._family`` (the resolved UI font
    family) before using these."""

    scale: float
    theme: dict[str, str]
    _family: str

    def _color(self, name: str) -> str:
        return self.theme[name]

    def _px(self, value: float) -> int:
        return max(1, round(value * self.scale))

    def _font(self, px: int, bold: bool = False):
        # Negative Tk font size == physical pixels: per-monitor exact sizing,
        # independent of Tk's global (primary-monitor) point scaling.
        font = (self._family, -self._px(px))
        return font + ("bold",) if bold else font


class CalendarPopup(ThemedPopup):
    """Borderless two-month calendar, anchored to the taskbar edge of the
    monitor the cursor is on and scaled to that monitor's effective DPI.

    Built fully while withdrawn (no first-paint flash); the label grid is
    created once and month navigation only reconfigures text/colours."""

    MONTHS_SHOWN = 2
    ROWS = 6
    #: Day-cell box size in px @ 96 DPI. Floors the today circle's diameter.
    #: 25 gives the day numbers a little more breathing room without turning
    #: the two-month flyout into a large wall calendar.
    CELL_PX = 25
    #: Vertical gap between the month/year title bar and weekday headers, in px
    #: @ 96 DPI. 6px follows the Fluent 8px-ish rhythm of the rest of the
    #: card; the previous 4px read as crowded next to the larger card padding.
    TITLE_WEEKDAY_GAP_PX = 6
    #: Hover dwell before a holiday-name tooltip appears, in milliseconds.
    TOOLTIP_DELAY_MS = 450
    _instance: "CalendarPopup | None" = None

    @classmethod
    def show(cls, master: tk.Misc, settings: "SettingsStore") -> None:
        if cls._instance is not None:
            old, cls._instance = cls._instance, None
            try:
                if old.top.winfo_exists():
                    old.top.destroy()
            except tk.TclError:
                pass
        cls._instance = cls(master, settings)

    @classmethod
    def refresh_open(cls) -> None:
        """Re-render the open popup in place after settings-backed display data
        changes. Usually a no-op: opening the tray menu drops the popup's focus,
        which closes it first. Still keep the live path correct because tray
        menu focus behaviour varies across Windows/Pystray versions."""
        inst = cls._instance
        if inst is None:
            return
        try:
            if inst.top.winfo_exists():
                inst._refresh_from_settings()
        except tk.TclError:
            pass

    def __init__(self, master: tk.Misc, settings: "SettingsStore") -> None:
        cursor, monitor, work, scale = cursor_monitor_geometry()
        self.scale = scale
        self.settings = settings
        self.theme = theme_colors(settings)
        self._work = work
        self.display_month = dt.date.today().replace(day=1)
        self.selected_date: dt.date | None = None
        self._had_focus = False
        self._tip: "tk.Toplevel | None" = None
        self._tip_after = None
        self._tip_visible = False
        self._context_menu_visible = False

        self.top = tk.Toplevel(master)
        self.top.withdraw()
        self.top.overrideredirect(True)
        self.top.attributes("-topmost", True)
        self._family = ui_font_family(self.top)

        self.card: "tk.Frame | None" = None
        self._build_card()

        bindings = (
            ("<Escape>", lambda e: self.close()),
            ("<FocusOut>", lambda e: None if (self._tip_visible or self._context_menu_visible) else self.close()),
            ("<MouseWheel>", self._on_mousewheel),
            ("<Left>", lambda e: self._shift_month(-1)),
            ("<Right>", lambda e: self._shift_month(1)),
            ("<Home>", lambda e: self._go_today()),
        )
        for sequence, handler in bindings:
            self.top.bind(sequence, handler)

        self._place(cursor, monitor, work)
        self.top.deiconify()
        self.top.lift()
        self.top.focus_force()

        self.top.update_idletasks()
        self._hwnd = toplevel_hwnd(self.top)
        set_rounded_corners(self._hwnd)
        self.top.after(200, self._watch_foreground)

    def _build_card(self) -> None:
        """(Re)create the surface and everything on it for the active theme.

        Shared by first construction and by theme switches: the rounded
        PhotoImages are pre-rendered with palette colours, so a theme change
        must rebuild the widget tree, not just repaint it. The toplevel keeps
        the border colour and the card sits 1px inside it -- the Fluent
        hairline surface stroke; DWM rounds the stroke with the window."""
        self.top.configure(bg=self._color("CARD_BORDER"))
        if self.card is not None:
            self.card.destroy()
        pad = self._px(10)
        self.card = tk.Frame(self.top, bg=self._color("CARD_BG"), padx=pad, pady=pad)
        self.card.pack(fill="both", expand=True, padx=1, pady=1)

        self._build_cell_images()
        self._build_skeleton()
        self._refresh()

    def _refresh_from_settings(self) -> None:
        """Refresh settings-driven UI state without losing the selected date.

        Holiday changes only need a data repaint. Theme changes are different:
        see _build_card.
        """
        new_theme = theme_colors(self.settings)
        if new_theme == self.theme:
            self._refresh()
            return

        cursor, monitor, work, scale = cursor_monitor_geometry()
        self.scale = scale
        self._work = work
        self.theme = new_theme
        self._hide_tip()

        self._build_card()
        self._place(cursor, monitor, work)
        self.top.lift()

    # -- construction -------------------------------------------------------
    def _build_cell_images(self) -> None:
        """Build the spacer, the rounded state pills, and the band corner-notches.

        Every day and week cell carries one centred image; its width pins the
        column to CELL_PX, so the grid stays uniform and never reflows. The
        state pills -- today, selected, holiday, hover -- are rounded squares
        whose transparent corners let the cell background show through; today
        is a solid accent fill, selected is a soft fill with an accent ring
        (Fluent's filled-vs-outlined today/selected grammar). The band
        corner-notches are the inverse: card-coloured cut-outs that round the
        outer ends of a highlighted week while it stays one continuous bg=band
        bar. All shapes come from three supersampled masks (full, left pair,
        right pair) plus one ring mask and two chevron strokes, composed into
        fifteen tiny instance-scoped PhotoImages that are freed with the
        popup. Falls back to flat background fills (see _refresh) when
        ImageTk is missing."""
        self._cell_imgs = {"blank": None}
        if ImageTk is None:
            return
        try:
            d = self._px(self.CELL_PX)
            r = self._px(5)               # corner radius @ this DPI
            ss = 4                        # supersample, LANCZOS down for clean edges

            def mask(side: str) -> "Image.Image":
                # Full rounded mask, then square off the corners we do NOT want
                # by painting that edge strip solid. This avoids
                # rounded_rectangle's `corners=` argument, which only exists in
                # Pillow >= 9.4; this path works back to 8.2, when
                # rounded_rectangle itself was added. Output is identical.
                n, rad = d * ss, r * ss
                big = Image.new("L", (n, n), 0)
                dr = ImageDraw.Draw(big)
                dr.rounded_rectangle((0, 0, n - 1, n - 1), radius=rad, fill=255)
                if side == "left":        # keep left corners; square the right
                    dr.rectangle((n - rad - 1, 0, n - 1, n - 1), fill=255)
                elif side == "right":     # keep right corners; square the left
                    dr.rectangle((0, 0, rad, n - 1), fill=255)
                return big.resize((d, d), Image.LANCZOS)

            def ring_mask() -> "Image.Image":
                # Outline-only rounded square, hugging the same silhouette as
                # the full pill (PIL strokes grow inward from the boundary).
                # ~1.25px @ 96 DPI reads as Fluent's focus/selection ring.
                n, rad = d * ss, r * ss
                width = max(1, round(1.25 * self.scale)) * ss
                big = Image.new("L", (n, n), 0)
                ImageDraw.Draw(big).rounded_rectangle(
                    (0, 0, n - 1, n - 1), radius=rad, outline=255, width=width)
                return big.resize((d, d), Image.LANCZOS)

            full = mask("full")
            left = mask("left")    # left pair only
            right = mask("right")  # right pair only
            ring = ring_mask()

            def layer(m: "Image.Image", color: str) -> "Image.Image":
                # Solid colour behind an alpha mask; a reusable compose input.
                out = Image.new("RGBA", (d, d), color)
                out.putalpha(m)
                return out

            card = self._color("CARD_BG")
            # Inverse-alpha band caps: card-coloured corner cut-outs. Laid over
            # a bg=band cell they paint card into the band's square outer
            # corners, so the row stays one continuous band fill while its end
            # reads as rounded. Band colour is the cell bg, so a single card
            # notch serves both highlight colours.
            notch_left = layer(left.point(lambda px: 255 - px), card)
            notch_right = layer(right.point(lambda px: 255 - px), card)

            def compose(*layers: "Image.Image") -> "TkPhotoImage":
                out = Image.new("RGBA", (d, d), (0, 0, 0, 0))
                for item in layers:
                    out.alpha_composite(item)
                return ImageTk.PhotoImage(out, master=self.top)

            today = layer(full, self._color("ACCENT"))
            selected = layer(full, self._color("SELECTED_DAY_FILL"))
            sel_ring = layer(ring, self._color("ACCENT"))
            holiday = layer(full, self._color("HOLIDAY_BG"))
            hover = layer(full, self._color("HOVER_BG"))

            def chevron(direction: int) -> "Image.Image":
                # Month-nav arrow as geometry, not a font glyph. Tk's
                # compound="center" centres the font LINE box (ascent+descent)
                # -- Segoe UI's baseline sits well below that box's middle,
                # and the angle glyphs' ink hangs around the x-height, so a
                # text chevron rides ~1.5px below the pill's optical centre
                # with no non-magic way to correct it.
                #
                # Construction: PIL's wide-polyline rasteriser is direction-
                # dependent (mirrored polylines yield measurably different
                # ink), so only ONE arm is ever drawn; the lower arm is its
                # exact flip, the opposite direction its exact mirror. The
                # reflection axes pass through the raster centre (n-1)/2 --
                # the masks' centre -- so vertical centring and prev/next
                # parity are structural, not numeric. Proportions follow
                # Fluent chevrons: ~1/3 of the target tall, ~1.5px stroke @
                # 96 DPI, rounded caps; the tip disc doubles as the joint.
                n = d * ss
                c = (n - 1) / 2
                half_h, half_w = 0.16 * n, 0.09 * n
                stroke = max(1.0, 1.5 * self.scale) * ss
                cap = stroke / 2
                back, tip = (c - half_w, c - half_h), (c + half_w, c)
                arm = Image.new("L", (n, n), 0)
                dr = ImageDraw.Draw(arm)
                dr.line([back, tip], fill=255, width=round(stroke))
                for ex, ey in (back, tip):
                    dr.ellipse((ex - cap, ey - cap, ex + cap, ey + cap), fill=255)
                glyph = ImageChops.lighter(arm, ImageOps.flip(arm))
                if direction < 0:
                    glyph = ImageOps.mirror(glyph)
                return layer(glyph.resize((d, d), Image.LANCZOS),
                             self._color("ACCENT"))

            chev_prev, chev_next = chevron(-1), chevron(+1)

            self._cell_imgs = {
                "blank": ImageTk.PhotoImage(
                    Image.new("RGBA", (d, d), (0, 0, 0, 0)), master=self.top),
                # Width-only spacer for the "CW" header. The week-number
                # column must be exactly as wide as the day cells; otherwise a
                # band-cap image can be centred inside a wider label and the
                # rounded cut-out appears inside the rectangle instead of at
                # the outer edge. Height 1 keeps the header row text-height.
                "cw_header_spacer": ImageTk.PhotoImage(
                    Image.new("RGBA", (d, 1), (0, 0, 0, 0)), master=self.top),
                # *_R variants also carry the card-coloured right corner
                # cut-outs used at the end of an active week band, keeping the
                # week bar rounded even when Sunday itself has a state pill.
                "today": compose(today),
                "today_R": compose(notch_right, today),
                "selected": compose(selected, sel_ring),
                "selected_R": compose(notch_right, selected, sel_ring),
                "holiday": compose(holiday),
                "holiday_R": compose(notch_right, holiday),
                "hover": compose(hover),
                "hover_R": compose(notch_right, hover),
                "notch_L": compose(notch_left),
                "notch_R": compose(notch_right),
                # Month-navigation chevrons: accent stroke on nothing (rest)
                # or on the shared hover pill.
                "nav_prev": compose(chev_prev),
                "nav_prev_hover": compose(hover, chev_prev),
                "nav_next": compose(chev_next),
                "nav_next_hover": compose(hover, chev_next),
            }
        except Exception:
            log.exception("Calendar pill images unavailable; using flat fills")
            self._cell_imgs = {"blank": None}

    def _build_skeleton(self) -> None:
        cell_font = self._font(11)
        self._img_mode = self._cell_imgs["blank"] is not None
        blank = self._cell_imgs["blank"]
        wrapper = tk.Frame(self.card, bg=self._color("CARD_BG"))
        wrapper.pack()

        self._titles: list[tuple[tk.Label, tk.Label]] = []
        self._week_cells: list[list[tk.Label]] = []
        self._day_cells: list[list[list[tk.Label]]] = []

        for m in range(self.MONTHS_SHOWN):
            grid_col = 2 * m
            if m:
                tk.Frame(wrapper, bg=self._color("SEPARATOR_BG"), width=1).grid(
                    row=0, column=grid_col - 1, sticky="ns", pady=self._px(10))

            frame = tk.Frame(wrapper, bg=self._color("CARD_BG"))
            frame.grid(row=0, column=grid_col, padx=self._px(6))

            # Tk Labels can't mix font weights, so the month (semibold) and
            # year (lighter) are two packed labels in a centred sub-frame.
            title_bar = tk.Frame(frame, bg=self._color("CARD_BG"))
            title_bar.grid(row=0, column=0, columnspan=8, sticky="ew",
                           pady=(0, self._px(self.TITLE_WEEKDAY_GAP_PX)))
            # Keep navigation inside the month title rows instead of dedicating
            # permanent outside columns to the chevrons. Those columns were
            # visible as dead strips down the left/right edges of the popup;
            # folding the controls into the title bar lets the day grid use
            # the reclaimed width without changing any navigation behaviour.
            title_bar.grid_columnconfigure(0, minsize=self._px(self.CELL_PX))
            title_bar.grid_columnconfigure(1, weight=1)
            title_bar.grid_columnconfigure(2, minsize=self._px(self.CELL_PX))

            self._make_chevron(title_bar, "\u2039", -1, column=0, visible=(m == 0))

            title_text = tk.Frame(title_bar, bg=self._color("CARD_BG"))
            title_text.grid(row=0, column=1)
            month_lbl = tk.Label(title_text, bg=self._color("CARD_BG"), fg=self._color("TITLE_FG"),
                                 font=self._font(14, bold=True))
            month_lbl.pack(side="left")
            year_lbl = tk.Label(title_text, bg=self._color("CARD_BG"), fg=self._color("YEAR_FG"), font=self._font(14))
            year_lbl.pack(side="left", padx=(self._px(4), 0))
            self._titles.append((month_lbl, year_lbl))

            self._make_chevron(
                title_bar, "\u203a", 1, column=2, visible=(m == self.MONTHS_SHOWN - 1))

            for c, name in enumerate(
                    ("CW", "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su")):
                # Day-name headers carry no marker and stay text-height. The
                # CW header is the exception: give it a transparent width-only
                # spacer so column 0 is pinned to the same pixel width as the
                # week-number cap images. If the "CW" text is allowed to make
                # the column wider, the left rounded cap is drawn inside the
                # blue week band and the real outside edge remains square.
                header = tk.Label(
                    frame, text=name, bg=self._color("CARD_BG"),
                    fg=self._color("CW_LABEL_FG") if c == 0 else self._color("DAY_NAME_FG"),
                    font=cell_font, bd=0, highlightthickness=0, padx=0, pady=0)
                if c == 0 and self._img_mode:
                    header.configure(
                        image=self._cell_imgs["cw_header_spacer"],
                        compound="center")
                header.grid(row=1, column=c, padx=0, sticky="nsew")

            week_column: list[tk.Label] = []
            day_grid: list[list[tk.Label]] = []
            for r in range(self.ROWS):
                week_label = tk.Label(frame, bg=self._color("CARD_BG"), fg=self._color("WEEK_NUM_FG"), font=cell_font,
                                      bd=0, highlightthickness=0, padx=0, pady=0)
                week_label.grid(row=2 + r, column=0, padx=0, sticky="nsew")
                week_label.date = None
                week_label.iso_year_week = None
                week_label.bind("<Button-3>", self._on_context_menu)
                week_column.append(week_label)

                row_cells: list[tk.Label] = []
                for c in range(7):
                    cell = tk.Label(
                        frame, bg=self._color("CARD_BG"), fg=self._color("TITLE_FG"), font=cell_font,
                        bd=0, highlightthickness=0, padx=0, pady=0)
                    if self._img_mode:
                        cell.configure(image=blank, compound="center")
                    else:
                        cell.configure(width=3)
                    cell.grid(row=2 + r, column=c + 1, padx=0, pady=0, sticky="nsew")
                    cell.base_bg = self._color("CARD_BG")
                    cell.hoverable = False
                    cell.date = None
                    cell.bind("<Enter>", self._on_cell_enter)
                    cell.bind("<Leave>", self._on_cell_leave)
                    cell.bind("<Button-1>", self._on_day_click)
                    cell.bind("<Button-3>", self._on_context_menu)
                    row_cells.append(cell)
                day_grid.append(row_cells)

            # Pin every week row to a uniform height. Months span 4-6 week
            # rows; without this the empty trailing rows collapse and the
            # popup's height -- anchored to the taskbar edge -- shifts as you
            # navigate. Fixed rows keep every month the same size.
            for r in range(self.ROWS):
                frame.grid_rowconfigure(2 + r, minsize=self._px(self.CELL_PX), uniform="day")

            self._week_cells.append(week_column)
            self._day_cells.append(day_grid)

        tk.Frame(self.card, bg=self._color("SEPARATOR_BG"), height=1).pack(
            fill="x", pady=(self._px(8), self._px(4)))
        self._footer = tk.Label(self.card, bg=self._color("CARD_BG"), fg=self._color("ACCENT"),
                                font=self._font(11, bold=True), cursor="hand2",
                                padx=self._px(10), pady=self._px(2))
        self._footer.pack()
        # Clicking today's date jumps back to the current month (same action
        # as the <Home> key); the grid's today cell is only visible when
        # already there, so the affordance lives here too. Hover feedback
        # makes the chip read as the button it is.
        self._footer.bind("<Button-1>", lambda e: self._go_today())
        self._footer.bind("<Enter>", lambda e: self._footer.configure(bg=self._color("HOVER_BG")))
        self._footer.bind("<Leave>", lambda e: self._footer.configure(bg=self._color("CARD_BG")))

    def _make_chevron(
            self, parent: tk.Misc, glyph: str, delta: int, column: int,
            visible: bool = True) -> tk.Label:
        """Month-navigation chevron. In image mode the arrow is geometry
        baked into the pill images (see _build_cell_images), so it is
        ink-centred in the hover pill by construction -- a text glyph over
        the image cannot be, because Tk centres the font line box, not the
        ink. The flat fallback keeps the old text glyph and rectangular
        hover; it inherits the small vertical offset, and only exists when
        ImageTk is missing."""
        chevron = tk.Label(parent, bg=self._color("CARD_BG"), bd=0,
                           highlightthickness=0,
                           cursor="hand2" if visible else "")
        if self._img_mode:
            key = "nav_prev" if delta < 0 else "nav_next"
            rest_img = self._cell_imgs[key] if visible else self._cell_imgs["blank"]
            hover_img = self._cell_imgs[key + "_hover"]
            chevron.configure(image=rest_img, padx=0, pady=0)
        else:
            chevron.configure(
                text=glyph, font=self._font(14, bold=True),
                fg=self._color("ACCENT") if visible else self._color("CARD_BG"),
                padx=self._px(2), pady=self._px(1))
        chevron.grid(row=0, column=column, sticky="nsew")
        if visible:
            if self._img_mode:
                chevron.bind("<Enter>", lambda e: chevron.configure(image=hover_img))
                chevron.bind("<Leave>", lambda e: chevron.configure(image=rest_img))
            else:
                chevron.bind("<Enter>", lambda e: chevron.configure(bg=self._color("HOVER_BG")))
                chevron.bind("<Leave>", lambda e: chevron.configure(bg=self._color("CARD_BG")))
            chevron.bind("<Button-1>", lambda e: self._shift_month(delta))
        return chevron

    # -- data fill ----------------------------------------------------------
    def _band_notch(self, band: str, side: str) -> str:
        """Image key for the card corner-notch that rounds the band's outer end
        on `side` ('L'/'R'), or the transparent spacer when the row is plain.
        The notch is card-coloured and rides on a bg=band cell, so the bar stays
        one continuous fill instead of splitting into separate boxes."""
        return ("notch_" + side) if band != self._color("CARD_BG") else "blank"

    def _hover_key(self, last_col: bool, band: str) -> str:
        """Hover pill key for a cell: the right-capped variant on the last
        column of a highlighted band, the plain pill everywhere else."""
        return "hover_R" if last_col and band != self._color("CARD_BG") else "hover"

    def _refresh(self) -> None:
        today = dt.date.today()
        # Compare (ISO year, week): the week number alone collides across
        # year boundaries and would highlight the wrong week in Dec/Jan.
        today_iso = today.isocalendar()[:2]
        sel = self.selected_date
        sel_iso = sel.isocalendar()[:2] if sel else None

        cell_font = self._font(11)
        country = self.settings.holiday_country

        # Manual day join: strftime %d zero-pads and the unpadded specifier
        # is platform-specific (%#d Windows, %-d POSIX).
        self._footer.configure(text="Today \u00b7 {:02d}-{:02d}-{}".format(
            today.day, today.month, today.year))

        for m in range(self.MONTHS_SHOWN):
            year = self.display_month.year + (self.display_month.month + m - 1) // 12
            month = (self.display_month.month + m - 1) % 12 + 1
            first = dt.date(year, month, 1)
            next_first = dt.date(year + 1, 1, 1) if month == 12 else dt.date(year, month + 1, 1)
            last = next_first - dt.timedelta(days=1)
            days_in_month = (next_first - first).days

            month_lbl, year_lbl = self._titles[m]
            month_lbl.configure(text=first.strftime("%B"))
            year_lbl.configure(text=str(year))

            # A week band belongs to the month pane that contains the actual
            # anchor date. ISO weeks often span two visible months (e.g. Jun 29
            # and Jul 1 are both CW27); highlighting every matching ISO week in
            # every pane makes the selection/current-week appear duplicated.
            # Restricting by pane month gives exactly one current band and
            # exactly one selected band.
            pane_is_today_month = (year, month) == (today.year, today.month)
            pane_is_selected_month = (
                sel is not None and (year, month) == (sel.year, sel.month)
            )

            grid_start = first - dt.timedelta(days=first.weekday())
            row_bands: list[tuple[tuple[int, int] | None, str]] = []

            for r in range(self.ROWS):
                row_start = grid_start + dt.timedelta(days=7 * r)
                row_end = row_start + dt.timedelta(days=6)
                row_has_month_day = row_start <= last and row_end >= first
                row_iso = row_start.isocalendar()[:2] if row_has_month_day else None

                if row_iso is not None and pane_is_today_month and row_iso == today_iso:
                    band = self._color("CURRENT_WEEK_BG")
                elif row_iso is not None and pane_is_selected_month and row_iso == sel_iso:
                    band = self._color("SELECTED_WEEK_BG")
                else:
                    band = self._color("CARD_BG")

                row_bands.append((row_iso, band))

                week_label = self._week_cells[m][r]
                week_label.date = None
                week_label.iso_year_week = row_iso
                week_text = str(row_iso[1]) if row_iso is not None else ""
                if self._img_mode:
                    # Column 0 is pinned to the same pixel width as the cap
                    # image by the CW header spacer. Draw the left notch across
                    # the full week-number cell and keep the number centred.
                    week_label.configure(
                        text=week_text,
                        bg=band,
                        fg=self._color("WEEK_NUM_FG"),
                        font=cell_font,
                        image=self._cell_imgs[self._band_notch(band, "L")],
                        compound="center",
                        anchor="center",
                    )
                else:
                    week_label.configure(text=week_text, bg=band, fg=self._color("WEEK_NUM_FG"), font=cell_font)

                for c, cell in enumerate(self._day_cells[m][r]):
                    rest_key = self._band_notch(band, "R") if c == 6 else "blank"
                    rest_img = self._cell_imgs[rest_key] if self._img_mode else None
                    cell.base_bg = band
                    cell.hoverable = False
                    cell.date = None
                    cell.iso_year_week = None
                    cell.holiday_name = None
                    cell.configure(text="", bg=band, fg=self._color("TITLE_FG"), font=cell_font)
                    if self._img_mode:
                        cell.configure(image=rest_img)
                        cell.rest_img = rest_img
                        cell.hover_img = self._cell_imgs[self._hover_key(c == 6, band)]

            row, col = 0, first.weekday()  # Monday == 0
            for day in range(1, days_in_month + 1):
                date = first.replace(day=day)
                iso_year, iso_week, _ = date.isocalendar()
                row_iso = (iso_year, iso_week)
                band = row_bands[row][1]

                is_today = date == today
                is_selected = sel is not None and date == sel and not is_today
                hol = holiday_name(country, date)

                cell = self._day_cells[m][row][col]
                fg, hoverable = self._color("TITLE_FG"), True
                if self._img_mode:
                    # Cell background is the continuous row band. Marker images
                    # are layered over it; on Sunday, *_R variants also paint
                    # card-coloured cut-outs into the band's outer right corners.
                    suffix = "_R" if col == 6 and band != self._color("CARD_BG") else ""
                    if is_today:
                        rest, fg, hoverable = "today" + suffix, self._color("TODAY_FG"), False
                    elif is_selected:
                        rest = "selected" + suffix
                    elif hol is not None:
                        rest, hoverable = "holiday" + suffix, False  # stays lilac on hover
                    else:
                        rest = self._band_notch(band, "R") if col == 6 else "blank"

                    rest_img = self._cell_imgs[rest]
                    cell.configure(text=str(day), bg=band, fg=fg,
                                   image=rest_img, font=cell_font)
                    cell.rest_img = rest_img
                    cell.hover_img = self._cell_imgs[self._hover_key(col == 6, band)]
                    cell.base_bg = band
                else:
                    bg = band
                    if is_today:
                        bg, fg, hoverable = self._color("ACCENT"), self._color("TODAY_FG"), False
                    elif is_selected:
                        bg = self._color("SELECTED_DAY_FILL")
                    elif hol is not None:
                        bg, hoverable = self._color("HOLIDAY_BG"), False
                    cell.configure(text=str(day), bg=bg, fg=fg, font=cell_font)
                    cell.base_bg = bg
                cell.hoverable = hoverable
                cell.date = date
                cell.iso_year_week = row_iso
                cell.holiday_name = hol

                col += 1
                if col == 7:
                    col = 0
                    row += 1

    # -- interaction --------------------------------------------------------
    def _copy_to_clipboard(self, value: str) -> None:
        try:
            self.top.clipboard_clear()
            self.top.clipboard_append(value)
            self.top.update_idletasks()
        except Exception:
            log.exception("Failed to copy calendar value to clipboard")

    def _on_context_menu(self, event) -> None:
        widget = event.widget
        on_date = getattr(widget, "date", None)
        iso_year_week = getattr(widget, "iso_year_week", None)
        if on_date is None and iso_year_week is None:
            return

        menu = tk.Menu(
            self.top,
            tearoff=False,
            bg=self._color("MENU_BG"),
            fg=self._color("MENU_FG"),
            activebackground=self._color("MENU_ACTIVE_BG"),
            activeforeground=self._color("MENU_ACTIVE_FG"),
            bd=0,
            relief="flat",
        )
        if on_date is not None:
            menu.add_command(
                label=f"Copy date ({format_gui_date(on_date)})",
                command=lambda d=on_date: self._copy_to_clipboard(format_gui_date(d)),
            )
            menu.add_command(
                label=f"Copy week ({format_calendar_week(on_date)})",
                command=lambda d=on_date: self._copy_to_clipboard(format_calendar_week(d)),
            )
        elif iso_year_week is not None:
            menu.add_command(
                label=f"Copy week ({format_iso_week(*iso_year_week)})",
                command=lambda yw=iso_year_week: self._copy_to_clipboard(format_iso_week(*yw)),
            )

        self._hide_tip()
        self._context_menu_visible = True
        try:
            menu.tk_popup(event.x_root, event.y_root)
        finally:
            menu.grab_release()
            self._context_menu_visible = False
            try:
                self.top.focus_force()
            except tk.TclError:
                pass

    def _on_cell_enter(self, event) -> None:
        widget = event.widget
        if getattr(widget, "hoverable", False):
            if self._img_mode:
                widget.configure(image=getattr(widget, "hover_img", self._cell_imgs["hover"]))
            else:
                widget.configure(bg=self._color("HOVER_BG"))
        name = getattr(widget, "holiday_name", None)
        if name:
            self._schedule_tip(widget, name)

    def _on_cell_leave(self, event) -> None:
        widget = event.widget
        if getattr(widget, "hoverable", False):
            if self._img_mode:
                widget.configure(
                    image=getattr(widget, "rest_img", self._cell_imgs["blank"]))
            else:
                widget.configure(bg=widget.base_bg)
        self._hide_tip()

    # -- holiday tooltip ----------------------------------------------------
    def _schedule_tip(self, widget: tk.Widget, text: str) -> None:
        self._cancel_tip_timer()
        self._tip_after = self.top.after(
            self.TOOLTIP_DELAY_MS, lambda: self._show_tip(widget, text))

    def _show_tip(self, widget: tk.Widget, text: str) -> None:
        # A borderless child window, never focused, so it can't trip the
        # popup's focus-out / foreground close (also guarded by _tip_visible).
        self._hide_tip()
        try:
            tip = tk.Toplevel(self.top)
            tip.overrideredirect(True)
            tip.attributes("-topmost", True)
            border = tk.Frame(tip, bg=self._color("TOOLTIP_BORDER"))
            border.pack()
            tk.Label(border, text=text, bg=self._color("TOOLTIP_BG"), fg=self._color("TITLE_FG"),
                     font=self._font(11), padx=self._px(8),
                     pady=self._px(4)).pack(padx=1, pady=1)
            tip.update_idletasks()
            tw, th = tip.winfo_reqwidth(), tip.winfo_reqheight()
            w = self._work
            x = max(w.left, min(widget.winfo_rootx(), w.right - tw))
            y = widget.winfo_rooty() + widget.winfo_height() + self._px(3)
            if y + th > w.bottom:  # no room below the cell -> sit above it
                y = widget.winfo_rooty() - th - self._px(3)
            y = max(w.top, y)
            tip.geometry(f"+{int(x)}+{int(y)}")
            set_rounded_corners(toplevel_hwnd(tip), small=True)
            self._tip = tip
            self._tip_visible = True
        except tk.TclError:
            self._tip = None
            self._tip_visible = False

    def _cancel_tip_timer(self) -> None:
        if self._tip_after is not None:
            try:
                self.top.after_cancel(self._tip_after)
            except Exception:
                pass
            self._tip_after = None

    def _hide_tip(self) -> None:
        self._cancel_tip_timer()
        if self._tip is not None:
            try:
                self._tip.destroy()
            except tk.TclError:
                pass
            self._tip = None
        self._tip_visible = False

    def _on_day_click(self, event) -> None:
        date = getattr(event.widget, "date", None)
        if date is None:  # blank lead-in/trail-out cell
            return
        self.selected_date = date
        self._refresh()

    def _on_mousewheel(self, event) -> None:
        self._shift_month(-1 if event.delta > 0 else 1)

    def _shift_month(self, delta: int) -> None:
        year, month = self.display_month.year, self.display_month.month + delta
        if month < 1:
            year, month = year - 1, 12
        elif month > 12:
            year, month = year + 1, 1
        self.display_month = dt.date(year, month, 1)
        self._refresh()

    def _go_today(self) -> None:
        self.display_month = dt.date.today().replace(day=1)
        self._refresh()

    # -- placement & lifetime -----------------------------------------------
    def _place(self, cursor: POINT, monitor: RECT, work: RECT) -> None:
        self.top.update_idletasks()
        width = self.top.winfo_reqwidth()
        height = self.top.winfo_reqheight()
        margin = self._px(8)

        # The taskbar lives on the side where the work area is inset.
        gaps = {
            "left": work.left - monitor.left,
            "top": work.top - monitor.top,
            "right": monitor.right - work.right,
            "bottom": monitor.bottom - work.bottom,
        }
        edge = max(gaps, key=gaps.get) if max(gaps.values()) > 0 else "bottom"

        if edge in ("bottom", "top"):
            x = cursor.x - width // 2
            y = work.bottom - height - margin if edge == "bottom" else work.top + margin
        else:
            y = cursor.y - height // 2
            x = work.left + margin if edge == "left" else work.right - width - margin

        x = max(work.left + margin, min(x, work.right - width - margin))
        y = max(work.top + margin, min(y, work.bottom - height - margin))

        self.top.geometry(f"{width}x{height}+{x}+{y}")

    def _watch_foreground(self) -> None:
        """Close when another window takes the foreground -- the behaviour of
        the native clock flyout. Covers the cases <FocusOut> misses."""
        try:
            if not self.top.winfo_exists():
                return
        except tk.TclError:
            return
        foreground = user32.GetForegroundWindow()
        if foreground == self._hwnd:
            self._had_focus = True
        elif self._had_focus and not self._tip_visible:
            self.close()
            return
        self.top.after(150, self._watch_foreground)

    def close(self) -> None:
        if CalendarPopup._instance is self:
            CalendarPopup._instance = None
        self._hide_tip()
        try:
            self.top.destroy()
        except tk.TclError:
            pass
