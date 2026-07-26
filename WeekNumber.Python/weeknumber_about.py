"""About window for WeekNumber."""

import tkinter as tk
import webbrowser
from typing import TYPE_CHECKING

# weeknumber_calendar owns the friendly Pillow-missing guard. Importing it
# first (for the shared styling base) means a missing Pillow produces the
# install hint instead of a raw ImportError from the bare import below.
from weeknumber_calendar import ThemedPopup, ui_font_family
from PIL import Image, ImageDraw, ImageOps

try:
    # Optional: drives the rounded GitHub button and the app icon image.
    # Missing on the rare Pillow build without Tk support; the window then
    # falls back to a flat bordered button and a text-only header -- the
    # same degradation contract as the calendar.
    from PIL import ImageTk
except Exception:
    ImageTk = None

from weeknumber_core import (
    APP_NAME,
    APP_URL,
    APP_VERSION,
    RECT,
    SettingsStore,
    cursor_monitor_geometry,
    log,
    resource_path,
    set_dialog_frame,
    set_titlebar_dark,
    theme_colors,
    toplevel_hwnd,
)

if TYPE_CHECKING:
    # Same TYPE_CHECKING dance as weeknumber_calendar: ImageTk is a variable
    # to static analyzers because of the None fallback above.
    from PIL.ImageTk import PhotoImage as TkPhotoImage


class AboutPopup(ThemedPopup):
    """Fixed About dialog: a real titled window, not a flyout.

    Chrome: title bar with only the standard close button (minimize and
    maximize are stripped via core's set_dialog_frame), its own taskbar
    button while open, and a DWM dark title bar when dark mode is on. It
    stays open until closed -- close button, Alt+F4, or Esc; there is no
    focus-loss dismissal.

    Placement/size: centred in the work area of the monitor the cursor is
    on, with the client area sized in physical pixels from that monitor's
    effective DPI, so the dialog has the same physical size on every
    display. Single instance: reopening focuses the open window."""

    #: Base client-area metrics in px @ 96 DPI; every value below is scaled
    #: by the target monitor's effective DPI at build time.
    WIDTH_PX = 380
    HEIGHT_PX = 180
    PAD_PX = 28                    # left/right content padding
    ICON_PX = 80                   # app-icon square
    ICON_TEXT_GAP_PX = 20
    NAME_VERSION_GAP_PX = 4
    BUTTON_H_PX = 36
    BUTTON_MARGIN_BOTTOM_PX = 16
    BUTTON_RADIUS_PX = 8

    _instance: "AboutPopup | None" = None

    @classmethod
    def show(cls, master: tk.Misc, settings: "SettingsStore") -> None:
        """Open the dialog, or focus the one already open."""
        inst = cls._instance
        if inst is not None:
            try:
                if inst.top.winfo_exists():
                    inst.top.deiconify()
                    inst.top.lift()
                    inst.top.focus_force()
                    return
            except tk.TclError:
                pass
            cls._instance = None
        cls._instance = cls(master, settings)

    @classmethod
    def refresh_open(cls) -> None:
        """Re-theme the open dialog (card and title bar) after a dark-mode
        toggle. No-op when it is not open."""
        inst = cls._instance
        if inst is None:
            return
        try:
            if inst.top.winfo_exists():
                inst._refresh_from_settings()
        except tk.TclError:
            pass

    def __init__(self, master: tk.Misc, settings: "SettingsStore") -> None:
        _cursor, _monitor, work, scale = cursor_monitor_geometry()
        self.scale = scale
        self.settings = settings
        self.theme = theme_colors(settings)

        self.top = tk.Toplevel(master)
        self.top.withdraw()
        self.top.title(f"About {APP_NAME}")
        self.top.resizable(False, False)
        # Fully transparent until final placement: the decorated frame can
        # only be measured after the first map (see _center_outer), and the
        # frame-style surgery needs a hide/show cycle -- alpha 0 keeps all of
        # that invisible instead of flashing a jumping window.
        self.top.attributes("-alpha", 0.0)
        self._family = ui_font_family(self.top)
        self._set_window_icon()

        self.card: "tk.Frame | None" = None
        self._icon_img: "TkPhotoImage | None" = None
        self._btn_imgs: "dict[str, TkPhotoImage]" = {}
        self._build_card()

        # The X button and Alt+F4 must clear the singleton, not just destroy.
        # (The hidden root ignores WM_DELETE_WINDOW; protocols are per
        # toplevel, so this window gets its own handler.)
        self.top.protocol("WM_DELETE_WINDOW", self.close)
        self.top.bind("<Escape>", lambda e: self.close())

        # Provisional centre using the client size; corrected for the real
        # frame after the first map.
        client_w, client_h = self._px(self.WIDTH_PX), self._px(self.HEIGHT_PX)
        x = work.left + max(0, (work.right - work.left - client_w) // 2)
        y = work.top + max(0, (work.bottom - work.top - client_h) // 2)
        self.top.geometry(f"{client_w}x{client_h}+{x}+{y}")

        # First map creates Tk's decorated wrapper; only then does the frame
        # HWND exist to style. Still invisible (alpha 0) throughout.
        self.top.deiconify()
        self.top.update_idletasks()
        self._hwnd = toplevel_hwnd(self.top)
        set_titlebar_dark(self._hwnd, self.settings.is_dark_mode)
        set_dialog_frame(self._hwnd)
        # The shell only re-reads WS_EX_APPWINDOW on a hide/show cycle; this
        # is what makes the taskbar button appear (see set_dialog_frame).
        self.top.withdraw()
        self.top.deiconify()

        self._center_outer(work)
        self.top.attributes("-alpha", 1.0)
        self.top.lift()
        self.top.focus_force()

    # -- construction ---------------------------------------------------------
    def _set_window_icon(self) -> None:
        """Title-bar/taskbar icon from app.ico; the stock Tk icon otherwise."""
        path = resource_path("app.ico")
        if path is None:
            return
        try:
            self.top.iconbitmap(str(path))
        except tk.TclError:
            log.exception("Could not apply app.ico to the About window")

    def _build_card(self) -> None:
        """(Re)create the client-area content for the active theme. Shared by
        first construction and dark-mode refreshes: the icon and the button
        pills are palette-baked PhotoImages, so a theme change must rebuild,
        not repaint (same contract as CalendarPopup._build_card)."""
        self.top.configure(bg=self._color("CARD_BG"))
        if self.card is not None:
            self.card.destroy()
        self._icon_img = None
        self._btn_imgs = {}

        # Fixed dialog: the client size is pinned by wm geometry plus
        # resizable(False, False), and every child is place()-managed, so
        # content can never resize the window.
        client_w, client_h = self._px(self.WIDTH_PX), self._px(self.HEIGHT_PX)
        self.card = tk.Frame(self.top, bg=self._color("CARD_BG"),
                             width=client_w, height=client_h)
        self.card.pack(fill="both", expand=True)

        pad = self._px(self.PAD_PX)
        btn_h = self._px(self.BUTTON_H_PX)
        btn_y = client_h - btn_h - self._px(self.BUTTON_MARGIN_BOTTOM_PX)

        # Icon + name/version block, vertically centred in the area above the
        # button: anchor "w" centres the row's own height on btn_y / 2, which
        # is exactly (btn_y - block_height) / 2 for the block's top edge.
        row = tk.Frame(self.card, bg=self._color("CARD_BG"))
        row.place(x=pad, y=btn_y // 2, anchor="w")

        self._icon_img = self._load_icon_image(self._px(self.ICON_PX))
        if self._icon_img is not None:
            tk.Label(row, image=self._icon_img, bg=self._color("CARD_BG"),
                     bd=0, highlightthickness=0).pack(side="left")

        text = tk.Frame(row, bg=self._color("CARD_BG"))
        text.pack(side="left", padx=(
            self._px(self.ICON_TEXT_GAP_PX) if self._icon_img is not None else 0, 0))
        tk.Label(text, text=APP_NAME, font=self._font(20, bold=True),
                 bg=self._color("CARD_BG"), fg=self._color("TITLE_FG"),
                 bd=0, highlightthickness=0).pack(anchor="w")
        tk.Label(text, text=f"Version {APP_VERSION}", font=self._font(12),
                 bg=self._color("CARD_BG"), fg=self._color("YEAR_FG"),
                 bd=0, highlightthickness=0).pack(
            anchor="w", padx=(self._px(2), 0),  # optical left-align with name
            pady=(self._px(self.NAME_VERSION_GAP_PX), 0))

        self._build_button(pad, btn_y, client_w - 2 * pad, btn_h)

    def _build_button(self, x: int, y: int, width: int, height: int) -> None:
        """'View on GitHub' as a rounded outlined pill: hairline ring at rest,
        theme fills on hover/press. Falls back to a flat bordered label when
        ImageTk is unavailable."""
        btn = tk.Label(self.card, text="View on GitHub  \u2197",
                       font=self._font(12), bg=self._color("CARD_BG"),
                       fg=self._color("TITLE_FG"), bd=0, highlightthickness=0,
                       cursor="hand2")
        imgs = self._button_images(width, height)
        if imgs is not None:
            self._btn_imgs = imgs
            btn.configure(image=imgs["rest"], compound="center")

            def on_release(event) -> None:
                # Standard button semantics: releasing outside cancels.
                inside = 0 <= event.x < width and 0 <= event.y < height
                btn.configure(image=self._btn_imgs["hover" if inside else "rest"])
                if inside:
                    self._open_github()

            btn.bind("<Enter>", lambda e: btn.configure(image=self._btn_imgs["hover"]))
            btn.bind("<Leave>", lambda e: btn.configure(image=self._btn_imgs["rest"]))
            btn.bind("<Button-1>", lambda e: btn.configure(image=self._btn_imgs["pressed"]))
            btn.bind("<ButtonRelease-1>", on_release)
        else:
            btn.configure(highlightthickness=1,
                          highlightbackground=self._color("CARD_BORDER"))
            btn.bind("<Enter>", lambda e: btn.configure(bg=self._color("HOVER_BG")))
            btn.bind("<Leave>", lambda e: btn.configure(bg=self._color("CARD_BG")))
            btn.bind("<Button-1>", lambda e: self._open_github())
        btn.place(x=x, y=y, width=width, height=height)

    def _button_images(self, width: int, height: int) -> "dict[str, TkPhotoImage] | None":
        if ImageTk is None:
            return None
        try:
            ss = 4  # supersample, LANCZOS down -- same recipe as the day pills
            radius = self._px(self.BUTTON_RADIUS_PX) * ss
            ring = max(1, round(1.0 * self.scale)) * ss

            def pill(fill: str) -> "TkPhotoImage":
                big = Image.new("RGBA", (width * ss, height * ss), (0, 0, 0, 0))
                ImageDraw.Draw(big).rounded_rectangle(
                    (0, 0, width * ss - 1, height * ss - 1), radius=radius,
                    fill=fill, outline=self._color("CARD_BORDER"), width=ring)
                return ImageTk.PhotoImage(
                    big.resize((width, height), Image.LANCZOS), master=self.top)

            return {"rest": pill(self._color("CARD_BG")),
                    "hover": pill(self._color("HOVER_BG")),
                    "pressed": pill(self._color("SELECTED_DAY_FILL"))}
        except Exception:
            log.exception("About button pills unavailable; using flat fallback")
            return None

    def _load_icon_image(self, size: int) -> "TkPhotoImage | None":
        """app.ico scaled to `size` px: largest embedded frame, LANCZOS,
        letterboxed on transparency if non-square. Returns None -- and the
        header lays out text-only -- when the file or ImageTk is unavailable."""
        if ImageTk is None:
            return None
        path = resource_path("app.ico")
        if path is None:
            log.info("app.ico not found; About window renders without an icon")
            return None
        try:
            with Image.open(path) as ico:
                src = ico.convert("RGBA")  # forces load of the largest frame
            src = ImageOps.contain(src, (size, size), Image.LANCZOS)
            out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
            out.paste(src, ((size - src.width) // 2, (size - src.height) // 2), src)
            return ImageTk.PhotoImage(out, master=self.top)
        except Exception:
            log.exception("Could not load app.ico for the About window")
            return None

    # -- interaction & lifetime -------------------------------------------------
    def _open_github(self) -> None:
        # Same contract as elsewhere: webbrowser swallows the OS error and
        # returns False, hence the explicit check.
        if not webbrowser.open(APP_URL):
            log.warning("Could not open %s in a browser", APP_URL)

    def _refresh_from_settings(self) -> None:
        new_theme = theme_colors(self.settings)
        if new_theme == self.theme:
            return
        self.theme = new_theme
        self._build_card()  # geometry untouched: same monitor, same scale
        set_titlebar_dark(self._hwnd, self.settings.is_dark_mode)
        self.top.lift()

    def _center_outer(self, work: RECT) -> None:
        """Centre the OUTER frame in the work area. wm geometry positions the
        outer frame but sizes the client area, so the provisional centre in
        __init__ ignores the title bar and borders; once mapped, the real
        decoration offsets are measurable (winfo_rootx/rooty = client origin,
        winfo_x/y = frame origin) and the position is corrected. The window
        is still alpha-0 here, so the shift is never visible."""
        self.top.update_idletasks()
        border = self.top.winfo_rootx() - self.top.winfo_x()
        caption = self.top.winfo_rooty() - self.top.winfo_y()
        outer_w = self.top.winfo_width() + 2 * border
        outer_h = self.top.winfo_height() + caption + border  # bottom == side
        x = work.left + max(0, (work.right - work.left - outer_w) // 2)
        y = work.top + max(0, (work.bottom - work.top - outer_h) // 2)
        self.top.geometry(f"+{x}+{y}")

    def close(self) -> None:
        if AboutPopup._instance is self:
            AboutPopup._instance = None
        try:
            self.top.destroy()
        except tk.TclError:
            pass
