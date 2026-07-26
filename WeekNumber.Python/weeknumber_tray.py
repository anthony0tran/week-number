"""System tray integration for WeekNumber."""

import os
import tempfile
from tkinter import colorchooser
from typing import TYPE_CHECKING

try:
    import pystray
    from pystray import MenuItem
except ImportError:
    import sys
    print("Missing dependency: pystray\nInstall with: pip install pystray pillow")
    sys.exit(1)

# weeknumber_calendar owns the friendly Pillow-missing guard; both project
# imports below pull it in before the bare PIL import, so a missing Pillow
# produces the install hint instead of a raw ImportError traceback.
from weeknumber_about import AboutPopup
from weeknumber_calendar import CalendarPopup
from PIL import Image, ImageDraw, ImageFont

from weeknumber_core import (
    APP_NAME,
    HOLIDAY_COUNTRIES,
    SETTINGS_DIR,
    WeekNumber,
    is_startup_enabled,
    log,
    normalize_hex,
    set_startup,
    show_error,
    tray_callback,
    tray_icon_size,
    windows_directory,
)

if TYPE_CHECKING:
    # pystray assigns Icon at import time from the selected backend, so
    # `pystray.Icon` is a variable to static analyzers. All backends derive
    # from this base class; use it for annotations only. Application lives in
    # weeknumber.py, which imports this module -- resolvable only under
    # TYPE_CHECKING, where the circularity is harmless.
    from pystray._base import Icon as PystrayIcon
    from weeknumber import Application


class IconRenderer:
    """Renders the week number at the exact physical tray size: 8x
    supersampled with a measured font fit, exact ink-box centring, and
    horizontal condensing so bold keeps the same glyph height as regular."""

    _SS = 8  # supersampling factor

    def __init__(self) -> None:
        self._image_cache: dict[tuple[int, str, bool, int], Image.Image] = {}
        self._font_cache: dict[tuple[int, bool], ImageFont.ImageFont] = {}

    def render(self, number: int, color: str, bold: bool, size: int) -> Image.Image:
        number = max(1, min(53, int(number)))
        color = normalize_hex(color)
        bold = bool(bold)
        size = max(16, min(256, int(size)))
        key = (number, color, bold, size)
        cached = self._image_cache.get(key)
        if cached is not None:
            return cached
        image = self._render(str(number), color, bold, size)
        if len(self._image_cache) > 64:
            self._image_cache.clear()
        self._image_cache[key] = image
        return image

    #: Bold may overshoot the width budget by this factor and is then
    #: condensed horizontally to fit. Bold digits are wider at equal height,
    #: so without this, two-digit bold weeks fit at a visibly smaller point
    #: size than regular. A <=15% squeeze keeps full height at full weight;
    #: single digits never need it.
    _BOLD_MAX_CONDENSE = 1.15

    def _render(self, text: str, color: str, bold: bool, size: int) -> Image.Image:
        canvas = size * self._SS
        max_width = int(canvas * self._BOLD_MAX_CONDENSE) if bold else canvas
        font, (left, top, right, bottom) = self._fit_font(text, bold, max_width, canvas)
        width, height = right - left, bottom - top

        glyph = Image.new("RGBA", (width, height), (0, 0, 0, 0))
        ImageDraw.Draw(glyph).text((-left, -top), text, fill=color, font=font)
        if width > canvas:  # condense bold instead of shrinking it
            glyph = glyph.resize((canvas, height), Image.LANCZOS)
            width = canvas

        image = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))
        image.paste(glyph, ((canvas - width) // 2, (canvas - height) // 2), glyph)
        # No post-downscale alpha sharpening: it was tried (v1.0.0) and
        # reverted -- hardening edge alpha thins the strokes, which reads as
        # worse on a dark taskbar despite measuring "crisper" by fringe count.
        return image.resize((size, size), Image.LANCZOS)

    def _fit_font(self, text: str, bold: bool, max_width: int, max_height: int):
        draw = ImageDraw.Draw(Image.new("RGBA", (4, 4)))
        size = max_height
        while size > 8:
            font = self._font(size, bold)
            box = draw.textbbox((0, 0), text, font=font)
            if box[2] - box[0] <= max_width and box[3] - box[1] <= max_height:
                return font, box
            size = int(size * 0.9)
        font = self._font(8, bold)
        return font, draw.textbbox((0, 0), text, font=font)

    def _font(self, size: int, bold: bool):
        key = (size, bold)
        font = self._font_cache.get(key)
        if font is None:
            font = self._load_font(size, bold)
            self._font_cache[key] = font
        return font

    @staticmethod
    def _load_font(size: int, bold: bool):
        fonts_dir = windows_directory() / "Fonts"
        names = ("segoeuib.ttf", "arialbd.ttf") if bold else ("segoeui.ttf", "arial.ttf")
        for name in names:
            path = fonts_dir / name
            if path.exists():
                try:
                    return ImageFont.truetype(str(path), size=size)
                except OSError:
                    pass
        log.warning("No system TrueType font found; falling back to PIL default")
        try:
            return ImageFont.load_default(size=size)  # Pillow >= 10.1
        except TypeError:
            return ImageFont.load_default()

class NativeSizeIcon(pystray.Icon):
    """pystray loads its ICO with LR_DEFAULTSIZE, i.e. at SM_CXICON (the
    LARGE icon size, 32px @ 96 DPI); the shell then shrinks that handle back
    to SM_CXSMICON. The bitmap is thus GDI-resampled twice -- upscaled, then
    downscaled -- and arrives blurry no matter how well it was rendered.

    This override writes an ICO containing exactly one frame at the image's
    own pixel size (IconRenderer already matches it to SM_CXSMICON) and loads
    it at that size, so the shell draws it 1:1 with no resampling. The
    explicit ``sizes=`` is required: PIL's default ICO save only emits
    standard frame sizes and would drop e.g. the 20px frame of a 125%-scaled
    system. Any failure falls back to pystray's stock path (blurry but
    functional), keeping the coupling to pystray internals non-fatal.

    Trust note: the ICO briefly exists in the per-user %TEMP% between write
    and LoadImage. mkstemp creates it exclusively, and %TEMP% is not
    writable by other users; a same-user process swapping it in that window
    is outside this app's threat model (same-user code implies full
    compromise anyway)."""

    def _assert_icon_handle(self):
        if self._icon_handle:
            return
        try:
            from pystray._util import win32 as ps_win32
            fd, path = tempfile.mkstemp(".ico")
            try:
                with os.fdopen(fd, "wb") as f:
                    self.icon.save(f, format="ICO", sizes=[self.icon.size])
                handle = ps_win32.LoadImage(
                    None, path, ps_win32.IMAGE_ICON,
                    self.icon.width, self.icon.height,
                    ps_win32.LR_LOADFROMFILE)
            finally:
                try:
                    os.remove(path)
                except OSError:
                    pass
            if handle:
                self._icon_handle = handle
                return
        except Exception:
            log.exception("Native-size icon load failed; using pystray default")
        super()._assert_icon_handle()

class TrayIcon:
    def __init__(self, app: "Application") -> None:
        self.app = app
        self.week = WeekNumber()
        self.renderer = IconRenderer()
        self.icon_size = tray_icon_size()
        self.icon: "PystrayIcon | None" = None
        self._last_image_key: tuple | None = None
        # Reverse map for on_set_holiday: pystray hands the menu item back by
        # its display text, not by our country code. Built once: the menu and
        # this map share a lifecycle -- HOLIDAY_COUNTRIES is populated by
        # load_holiday_data() in main() before this class is constructed and
        # is immutable afterwards. If holiday data ever becomes reloadable at
        # runtime, rebuild BOTH the menu and this map together.
        self._holiday_label_to_code: dict[str, str] = {
            name: code for code, name in HOLIDAY_COUNTRIES
        }

    def _tooltip(self) -> str:
        text = (f"Week {self.week.number} \u00b7 updated "
                f"{self.week.last_updated.strftime('%d-%m-%Y %H:%M')}")
        return text[:63]  # NOTIFYICONDATA szTip limit

    def _image(self) -> Image.Image:
        settings = self.app.settings
        return self.renderer.render(self.week.number, settings.selected_color,
                                    settings.is_bold, self.icon_size)

    def _image_key(self) -> tuple:
        settings = self.app.settings
        return (self.week.number, settings.selected_color, settings.is_bold)

    def refresh(self) -> None:
        """Update tooltip always, the icon bitmap only when its inputs
        changed. The menu is never reassigned: pystray evaluates `checked`
        callables each time the menu opens."""
        if self.icon is None:
            return
        key = self._image_key()
        if key != self._last_image_key:
            self._last_image_key = key
            self.icon.icon = self._image()
        self.icon.title = self._tooltip()

    # -- menu callbacks (pystray thread) --------------------------------------
    @tray_callback
    def on_show_calendar(self, icon=None, item=None) -> None:
        self.week.refresh()
        self.refresh()
        self.app.run_on_ui(lambda: CalendarPopup.show(self.app.root, self.app.settings))

    @tray_callback
    def on_toggle_startup(self, icon=None, item=None) -> None:
        target = not is_startup_enabled()  # toggle from registry truth, not a cached flag
        if not set_startup(target):
            self.app.run_on_ui(lambda: show_error(
                "Could not update the startup setting. See the log in "
                f"{SETTINGS_DIR} for details."))

    @tray_callback
    def on_toggle_bold(self, icon=None, item=None) -> None:
        self.app.settings.is_bold = not self.app.settings.is_bold
        self.app.settings.save()
        self.refresh()

    @tray_callback
    def on_toggle_dark_mode(self, icon=None, item=None) -> None:
        self.app.settings.is_dark_mode = not self.app.settings.is_dark_mode
        self.app.settings.save()

        def retheme() -> None:
            CalendarPopup.refresh_open()
            AboutPopup.refresh_open()
        self.app.run_on_ui(retheme)

    @tray_callback
    def on_change_color(self, icon=None, item=None) -> None:
        def choose() -> None:
            result = colorchooser.askcolor(color=self.app.settings.selected_color,
                                           title="Choose icon color", parent=self.app.root)
            if result and result[1]:
                self.app.settings.selected_color = result[1]
                self.app.settings.save()
                self.refresh()
        self.app.run_on_ui(choose)

    @tray_callback
    def on_show_about(self, icon=None, item=None) -> None:
        self.app.run_on_ui(lambda: AboutPopup.show(self.app.root, self.app.settings))

    @tray_callback
    def on_exit(self, icon=None, item=None) -> None:
        self.app.request_shutdown()

    @tray_callback
    def on_set_holiday(self, icon=None, item=None) -> None:
        label = str(item.text) if item is not None else ""
        code = self._holiday_label_to_code.get(label)  # None for "Off"
        self.app.settings.holiday_country = code
        self.app.settings.save()
        self.app.run_on_ui(CalendarPopup.refresh_open)

    def _holiday_menu(self) -> pystray.Menu:
        def checked(item) -> bool:
            return (self.app.settings.holiday_country
                    == self._holiday_label_to_code.get(str(item.text)))

        items = [MenuItem("Off", self.on_set_holiday, checked=checked, radio=True)]
        for _code, name in HOLIDAY_COUNTRIES:
            items.append(MenuItem(name, self.on_set_holiday, checked=checked, radio=True))
        return pystray.Menu(*items)

    def _menu(self) -> pystray.Menu:
        return pystray.Menu(
            # Hidden: stays the left-click default action without a menu row.
            MenuItem("Show Calendar", self.on_show_calendar,
                     default=True, visible=False),
            MenuItem("Run at Startup", self.on_toggle_startup,
                     checked=lambda item: is_startup_enabled()),
            MenuItem("Bold Text", self.on_toggle_bold,
                     checked=lambda item: self.app.settings.is_bold),
            MenuItem("Change Color\u2026", self.on_change_color),
            MenuItem("Dark Mode", self.on_toggle_dark_mode,
                     checked=lambda item: self.app.settings.is_dark_mode),
            MenuItem("Public Holidays", self._holiday_menu()),
            pystray.Menu.SEPARATOR,
            # The single info entry: app version and the GitHub link live in
            # the About dialog, not as separate menu rows.
            MenuItem(f"About {APP_NAME}", self.on_show_about),
            pystray.Menu.SEPARATOR,
            MenuItem("Exit", self.on_exit),
        )

    def run(self) -> None:
        self._last_image_key = self._image_key()
        self.icon = NativeSizeIcon(APP_NAME, self._image(), self._tooltip(), self._menu())
        self.icon.run_detached()

    def stop(self) -> None:
        if self.icon is not None:
            try:
                self.icon.visible = False
                self.icon.stop()
            except Exception:
                log.exception("Tray icon stop failed")
            self.icon = None
