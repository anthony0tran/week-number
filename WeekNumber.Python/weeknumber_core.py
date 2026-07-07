"""Core infrastructure for WeekNumber.

Requirements: Windows 10+, Python 3.10+, pystray, Pillow.

Design notes
------------
* Per-Monitor V2 DPI awareness is enabled before Tk starts; the calendar
  popup sizes fonts/paddings in physical pixels for the monitor it appears
  on, so it renders sharp on every monitor.
* The tray icon is rendered at the shell's exact SM_CXSMICON size, 8x
  supersampled, and loaded at that size (see NativeSizeIcon) so neither
  GDI nor Explorer ever resamples it.
* The Run registry key is the single source of truth for "run at startup".
  StartupApproved is only written on an explicit user enable, so a
  "Disabled" choice made in Task Manager is respected.
* The calendar grid is built once per popup; month navigation only
  reconfigures label text/colours.
* The DLL search path is restricted to System32 before any native extension
  loads (see the hardening block below), so a DLL planted next to the .exe in
  a writable directory cannot be loaded in place of the genuine system copy.
* This module deliberately owns only shared, GUI-toolkit-agnostic
  infrastructure. Tk, pystray and Pillow are imported by the modules that
  use them, so each file's dependency surface is explicit and analysable.
"""

import ctypes
import os
import sys

if os.name != "nt":
    print("WeekNumber is a Windows-only application.")
    sys.exit(1)

# Harden the DLL search path to System32 BEFORE importing anything that loads
# native extensions or their dependent DLLs (tkinter -> Tcl/Tk, Pillow,
# pystray, hashlib -> OpenSSL). This removes the loading directory, the current
# working directory and PATH from the default search, so a DLL planted next to
# the .exe in a writable directory cannot be loaded in place of the genuine
# system copy. kernel32 is a KnownDLL -- section-mapped, never path-searched --
# so loading it by name here is itself safe. No-op before Win8 (no KB2533623).
# Note: this cannot protect the PyInstaller bootloader's own loads, which run
# before any of this code; sign the binary and install it to an ACL-protected
# directory to cover that surface.
LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800
try:
    ctypes.WinDLL("kernel32", use_last_error=True).SetDefaultDllDirectories(
        LOAD_LIBRARY_SEARCH_SYSTEM32)
except (OSError, AttributeError):
    pass

import datetime as dt
import functools
import hashlib
import json
import logging
import re
import tempfile
import threading
import time
import winreg
from ctypes import wintypes
from logging.handlers import RotatingFileHandler
from pathlib import Path

# Offline public-holiday tables. Security note: do not import holiday_data.py as
# executable code from a potentially writable application directory. If holiday
# support is needed, place non-executable JSON next to this script/executable:
# {"HOLIDAYS": {...}, "COUNTRIES": [["NL", "Netherlands"], ...]}

# ---------------------------------------------------------------------------
# Metadata & constants
# ---------------------------------------------------------------------------
APP_NAME = "WeekNumber"
APP_VERSION = "1.0"
#: Project home; linked from the tray menu. Lives here with APP_NAME and
#: APP_VERSION -- app metadata has exactly one home.
APP_URL = "https://github.com/anthony0tran/week-number"

STARTUP_APP_NAME = "WeekNumber"

HKCU = winreg.HKEY_CURRENT_USER
RUN_KEY = r"Software\Microsoft\Windows\CurrentVersion\Run"
STARTUP_APPROVED_KEY = r"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"
#: StartupApproved layout: 4-byte flag (0x02 enabled / 0x03 disabled) + 8-byte FILETIME.
APPROVED_ENABLED_VALUE = b"\x02" + b"\x00" * 11

def _safe_object_tag() -> str:
    """Per-user/per-install tag for named Win32 objects.

    Static object names are easy for another same-session process to pre-create
    or signal. This tag does not authenticate callers by itself, but it removes
    the predictable global name and separates different users/install paths.
    """
    try:
        launch = sys.executable if getattr(sys, "frozen", False) else __file__
        seed = "|".join((
            os.environ.get("USERDOMAIN", ""),
            os.environ.get("USERNAME", ""),
            str(Path.home()),
            str(Path(launch).resolve()),
        ))
    except Exception:
        seed = f"{os.getpid()}|{time.time()}"
    return hashlib.sha256(seed.encode("utf-8", "ignore")).hexdigest()[:16]


_OBJECT_TAG = _safe_object_tag()
MUTEX_NAME = f"Local\\WeekNumber_{_OBJECT_TAG}_SingleInstance"
#: Auto-reset event a launching instance sets to evict the running one.
QUIT_EVENT_NAME = f"Local\\WeekNumber_{_OBJECT_TAG}_QuitEvent"

# Palette
#
# The calendar consumes colours exclusively through theme_colors(settings).
# (The old module-level LIGHT_THEME aliases -- CARD_BG, ACCENT, ... -- had no
# remaining call sites and were removed in 1.1.)
LIGHT_THEME = {
    # Fluent-leaning light palette: one accent family, quiet neutral text
    # tiers, and a hairline surface stroke. Public-holiday purple is
    # intentionally distinct from the accent family.
    "CARD_BG": "#F9FAFC",
    "CARD_BORDER": "#E1E6EF",       # 1px surface stroke, as on Win11 flyouts
    "ACCENT": "#2F6FED",
    "TITLE_FG": "#1F2937",
    "YEAR_FG": "#6B7280",
    "DAY_NAME_FG": "#6E7A8A",       # secondary text, not accent: headers are
    "WEEK_NUM_FG": "#8A94A6",       # wayfinding, only *today* carries accent
    "CW_LABEL_FG": "#8A94A6",
    "TODAY_FG": "#FFFFFF",
    "CURRENT_WEEK_BG": "#EAF2FF",
    "SELECTED_WEEK_BG": "#F1F6FF",
    "SELECTED_DAY_FILL": "#E3EEFF",
    "HOLIDAY_BG": "#E9D8FD",
    "TOOLTIP_BG": "#FFFFFF",
    "TOOLTIP_BORDER": "#D6DEEA",
    "HOVER_BG": "#EAF0F9",
    "SEPARATOR_BG": "#E7EBF2",
    "MENU_BG": "#FFFFFF",
    "MENU_FG": "#1F2937",
    "MENU_ACTIVE_BG": "#E7F0FF",
    "MENU_ACTIVE_FG": "#1F2937",
}

DARK_THEME = {
    # Neutral dark-gray dark mode. Keep the accent family limited to blue and
    # the public-holiday marker purple, so the palette does not pick up a
    # separate blue-tinted background hue.
    "CARD_BG": "#1F1F1F",
    "CARD_BORDER": "#3A3A3A",
    "ACCENT": "#6EA8FF",
    "TITLE_FG": "#F5F5F5",
    "YEAR_FG": "#B8B8B8",
    "DAY_NAME_FG": "#9AA3B0",
    "WEEK_NUM_FG": "#8B94A3",
    "CW_LABEL_FG": "#8B94A3",
    "TODAY_FG": "#FFFFFF",
    "CURRENT_WEEK_BG": "#26364E",
    "SELECTED_WEEK_BG": "#232B3A",
    "SELECTED_DAY_FILL": "#2D4B73",
    "HOLIDAY_BG": "#3E2F50",
    "TOOLTIP_BG": "#262626",
    "TOOLTIP_BORDER": "#3A3A3A",
    "HOVER_BG": "#2C333D",
    "SEPARATOR_BG": "#333333",
    "MENU_BG": "#242424",
    "MENU_FG": "#F5F5F5",
    "MENU_ACTIVE_BG": "#2D4B73",
    "MENU_ACTIVE_FG": "#FFFFFF",
}


def theme_colors(settings: "SettingsStore | None" = None) -> dict[str, str]:
    """Return a copy of the active calendar palette.

    A copy keeps the palette immutable from the caller's perspective. The
    popup stores one snapshot per instance because its rounded Tk PhotoImages
    are colour-specific and must be rebuilt when the theme changes.
    """
    return dict(DARK_THEME if settings is not None and settings.is_dark_mode else LIGHT_THEME)

SETTINGS_DIR = Path(os.getenv("APPDATA", str(Path.home()))) / "WeekNumberPy"
SETTINGS_FILE = SETTINGS_DIR / "settings.json"
LOG_FILE = SETTINGS_DIR / "weeknumber.log"
MAX_SETTINGS_BYTES = 64 * 1024

log = logging.getLogger("weeknumber")


def setup_logging() -> None:
    SETTINGS_DIR.mkdir(parents=True, exist_ok=True)
    handler = RotatingFileHandler(LOG_FILE, maxBytes=256 * 1024, backupCount=1, encoding="utf-8")
    handler.setFormatter(logging.Formatter("%(asctime)s %(levelname)s: %(message)s"))
    log.addHandler(handler)
    log.setLevel(logging.INFO)

def tray_callback(func):
    """Error barrier for pystray-thread callbacks (uncaught exceptions there
    die silently) with a fixed (self, icon, item) arity.

    The arity is load-bearing: pystray validates actions via raw
    ``__code__.co_argcount``, which neither counts ``*args`` nor follows
    functools.wraps' ``__wrapped__``. A ``*args`` wrapper yields argcount -1
    for a bound method and pystray rejects it with ValueError at startup."""
    @functools.wraps(func)
    def wrapper(self, icon=None, item=None):
        try:
            return func(self, icon, item)
        except Exception:
            log.exception("Unhandled error in %s", func.__name__)
    return wrapper



# ---------------------------------------------------------------------------
# Win32 interop
# ---------------------------------------------------------------------------
# user32/kernel32/gdi32/shell32/ole32 are KnownDLLs (section-mapped, immune to
# planting). dwmapi and shcore are NOT KnownDLLs, so pin them to System32 with
# an explicit load flag -- redundant with the process-wide
# SetDefaultDllDirectories above, but robust to any dependency that mutates the
# default search (SetDllDirectory/AddDllDirectory) before these loads run.
user32 = ctypes.WinDLL("user32", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
gdi32 = ctypes.WinDLL("gdi32", use_last_error=True)
shell32 = ctypes.WinDLL("shell32", use_last_error=True)
ole32 = ctypes.WinDLL("ole32", use_last_error=True)
dwmapi = ctypes.WinDLL("dwmapi", use_last_error=True, winmode=LOAD_LIBRARY_SEARCH_SYSTEM32)
try:
    shcore = ctypes.WinDLL("shcore", use_last_error=True, winmode=LOAD_LIBRARY_SEARCH_SYSTEM32)
except OSError:
    shcore = None


class POINT(ctypes.Structure):
    _fields_ = (("x", ctypes.c_long), ("y", ctypes.c_long))


class RECT(ctypes.Structure):
    _fields_ = (
        ("left", ctypes.c_long),
        ("top", ctypes.c_long),
        ("right", ctypes.c_long),
        ("bottom", ctypes.c_long),
    )


class MONITORINFO(ctypes.Structure):
    _fields_ = (
        ("cbSize", wintypes.DWORD),
        ("rcMonitor", RECT),
        ("rcWork", RECT),
        ("dwFlags", wintypes.DWORD),
    )


class GUID(ctypes.Structure):
    """Win32 GUID, built from its "{...}" string form via ole32.IIDFromString."""
    _fields_ = (
        ("Data1", wintypes.DWORD),
        ("Data2", wintypes.WORD),
        ("Data3", wintypes.WORD),
        ("Data4", ctypes.c_ubyte * 8),
    )

    def __init__(self, guid: str) -> None:
        super().__init__()
        # S_OK is 0. On failure the GUID stays zeroed; callers observe the
        # subsequent shell lookup failing and fall back. Log it anyway --
        # a malformed constant is a programming error worth surfacing.
        if ole32.IIDFromString(guid, ctypes.byref(self)) != 0:
            log.warning("IIDFromString rejected GUID literal %r", guid)


user32.GetCursorPos.argtypes = (ctypes.POINTER(POINT),)
user32.GetCursorPos.restype = wintypes.BOOL
user32.MonitorFromPoint.argtypes = (POINT, wintypes.DWORD)
user32.MonitorFromPoint.restype = ctypes.c_void_p
user32.GetMonitorInfoW.argtypes = (ctypes.c_void_p, ctypes.POINTER(MONITORINFO))
user32.GetMonitorInfoW.restype = wintypes.BOOL
user32.GetForegroundWindow.restype = ctypes.c_void_p
user32.GetParent.argtypes = (ctypes.c_void_p,)
user32.GetParent.restype = ctypes.c_void_p
user32.GetDC.restype = ctypes.c_void_p
user32.ReleaseDC.argtypes = (ctypes.c_void_p, ctypes.c_void_p)
user32.MessageBoxW.argtypes = (ctypes.c_void_p, ctypes.c_wchar_p, ctypes.c_wchar_p, wintypes.UINT)
user32.SystemParametersInfoW.argtypes = (wintypes.UINT, wintypes.UINT, ctypes.c_void_p, wintypes.UINT)
user32.SetWindowPos.argtypes = (ctypes.c_void_p, ctypes.c_void_p, ctypes.c_int,
                                ctypes.c_int, ctypes.c_int, ctypes.c_int, wintypes.UINT)
user32.SetWindowPos.restype = wintypes.BOOL
# 32-bit builds export no *Ptr variants; the SDK macro maps to GetWindowLongW there.
_GetWindowLongPtrW = getattr(user32, "GetWindowLongPtrW", user32.GetWindowLongW)
_SetWindowLongPtrW = getattr(user32, "SetWindowLongPtrW", user32.SetWindowLongW)
_GetWindowLongPtrW.argtypes = (ctypes.c_void_p, ctypes.c_int)
_GetWindowLongPtrW.restype = ctypes.c_ssize_t
_SetWindowLongPtrW.argtypes = (ctypes.c_void_p, ctypes.c_int, ctypes.c_ssize_t)
_SetWindowLongPtrW.restype = ctypes.c_ssize_t
kernel32.CreateMutexW.argtypes = (ctypes.c_void_p, wintypes.BOOL, wintypes.LPCWSTR)
kernel32.CreateMutexW.restype = ctypes.c_void_p
kernel32.CloseHandle.argtypes = (ctypes.c_void_p,)
kernel32.CreateEventW.argtypes = (ctypes.c_void_p, wintypes.BOOL, wintypes.BOOL, wintypes.LPCWSTR)
kernel32.CreateEventW.restype = ctypes.c_void_p
kernel32.SetEvent.argtypes = (ctypes.c_void_p,)
kernel32.SetEvent.restype = wintypes.BOOL
kernel32.WaitForSingleObject.argtypes = (ctypes.c_void_p, wintypes.DWORD)
kernel32.WaitForSingleObject.restype = wintypes.DWORD
kernel32.GetWindowsDirectoryW.argtypes = (ctypes.c_wchar_p, wintypes.UINT)
kernel32.GetWindowsDirectoryW.restype = wintypes.UINT
dwmapi.DwmSetWindowAttribute.argtypes = (ctypes.c_void_p, wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD)
ole32.IIDFromString.argtypes = (wintypes.LPCWSTR, ctypes.POINTER(GUID))
ole32.IIDFromString.restype = ctypes.c_long
ole32.CoTaskMemFree.argtypes = (ctypes.c_void_p,)
shell32.SHGetKnownFolderPath.argtypes = (
    ctypes.POINTER(GUID), wintypes.DWORD, ctypes.c_void_p, ctypes.POINTER(ctypes.c_void_p))
shell32.SHGetKnownFolderPath.restype = ctypes.c_long

MONITOR_DEFAULTTONEAREST = 2
MDT_EFFECTIVE_DPI = 0
SPI_GETWORKAREA = 48
SM_CXSMICON = 49
LOGPIXELSX = 88
DWMWA_WINDOW_CORNER_PREFERENCE = 33
DWMWCP_ROUND = 2
DWMWCP_ROUNDSMALL = 3
#: Documented value (Win11 / Win10 20H1+); 19 is the pre-20H1 private value
#: used by 1809-1909. Older systems simply keep a light title bar.
DWMWA_USE_IMMERSIVE_DARK_MODE = 20
DWMWA_USE_IMMERSIVE_DARK_MODE_PRE20H1 = 19
GWL_STYLE = -16
GWL_EXSTYLE = -20
WS_MINIMIZEBOX = 0x00020000
WS_MAXIMIZEBOX = 0x00010000
WS_EX_APPWINDOW = 0x00040000
SWP_NOSIZE = 0x0001
SWP_NOMOVE = 0x0002
SWP_NOZORDER = 0x0004
SWP_NOACTIVATE = 0x0010
SWP_FRAMECHANGED = 0x0020
DWMWA_USE_IMMERSIVE_DARK_MODE = 20
DWMWA_USE_IMMERSIVE_DARK_MODE_1809 = 19  # pre-20H1 builds used 19
DWMWA_CAPTION_COLOR = 35                 # Win11 (build 22000+) only
DWMWA_TEXT_COLOR = 36                    # Win11 (build 22000+) only
ERROR_ALREADY_EXISTS = 183
WAIT_OBJECT_0 = 0
INFINITE = 0xFFFFFFFF
MB_ICONERROR = 0x10


def enable_dpi_awareness() -> None:
    """Per-Monitor V2 -> Per-Monitor -> System. Must run before Tk is
    created, otherwise Windows bitmap-stretches the UI on scaled monitors."""
    try:
        if user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4)):
            return
    except (AttributeError, OSError):
        pass
    try:
        if shcore is not None and shcore.SetProcessDpiAwareness(2) == 0:
            return
    except OSError:
        pass
    try:
        user32.SetProcessDPIAware()
    except OSError:
        pass


def system_dpi() -> int:
    try:
        return int(user32.GetDpiForSystem())  # Win10 1607+
    except (AttributeError, OSError):
        pass
    try:
        hdc = user32.GetDC(None)
        try:
            return int(gdi32.GetDeviceCaps(hdc, LOGPIXELSX))
        finally:
            user32.ReleaseDC(None, hdc)
    except OSError:
        return 96


def windows_directory() -> Path:
    """Return the real Windows directory without trusting WINDIR."""
    buf = ctypes.create_unicode_buffer(32768)
    n = kernel32.GetWindowsDirectoryW(buf, len(buf))
    if 0 < n < len(buf):
        return Path(buf.value)
    return Path(r"C:\Windows")


# Known Folders we refuse to autostart from. Resolved via the shell so
# redirected (OneDrive/Group Policy) Desktop and Downloads are handled --
# Path.home()/"Downloads" is simply the wrong path on the many systems where
# those folders are redirected, which would let the guardrail pass silently.
FOLDERID_DESKTOP = "{B4BFCC3A-DB2C-424C-B029-7FE99A87C641}"
FOLDERID_DOWNLOADS = "{374DE290-123F-4565-9164-39C4925E467B}"
FOLDERID_PUBLIC = "{DFDF76A2-C82A-4D63-906A-5644AC457385}"


def known_folder_path(folder_id: str) -> "Path | None":
    """Resolve a Known Folder to its real filesystem path, honouring
    redirection. Returns None if the shell lookup fails."""
    ptr = ctypes.c_void_p()
    try:
        hr = shell32.SHGetKnownFolderPath(
            ctypes.byref(GUID(folder_id)), 0, None, ctypes.byref(ptr))
        if hr != 0 or not ptr.value:
            return None
        return Path(ctypes.wstring_at(ptr))
    except OSError:
        return None
    finally:
        if ptr.value:
            ole32.CoTaskMemFree(ptr)


def monitor_scale(hmonitor) -> float:
    if shcore is not None:
        try:
            x, y = ctypes.c_uint(), ctypes.c_uint()
            if shcore.GetDpiForMonitor(ctypes.c_void_p(hmonitor), MDT_EFFECTIVE_DPI,
                                       ctypes.byref(x), ctypes.byref(y)) == 0:
                return x.value / 96.0
        except OSError:
            pass
    return system_dpi() / 96.0


def cursor_monitor_geometry() -> tuple[POINT, RECT, RECT, float]:
    """(cursor, monitor rect, work-area rect, scale) for the monitor under
    the cursor -- not the primary monitor."""
    pt = POINT()
    user32.GetCursorPos(ctypes.byref(pt))
    hmon = user32.MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST)
    mi = MONITORINFO()
    mi.cbSize = ctypes.sizeof(MONITORINFO)
    if hmon and user32.GetMonitorInfoW(hmon, ctypes.byref(mi)):
        return pt, mi.rcMonitor, mi.rcWork, monitor_scale(hmon)
    work = RECT()
    user32.SystemParametersInfoW(SPI_GETWORKAREA, 0, ctypes.byref(work), 0)
    mon = RECT(0, 0, user32.GetSystemMetrics(0), user32.GetSystemMetrics(1))
    return pt, mon, work, system_dpi() / 96.0


def tray_icon_size() -> int:
    """Physical pixel size the shell draws tray icons at (SM_CXSMICON)."""
    size = user32.GetSystemMetrics(SM_CXSMICON)
    if 8 <= size <= 256:
        return size
    return max(16, min(64, round(16 * system_dpi() / 96.0)))


def set_rounded_corners(hwnd, small: bool = False) -> None:
    """Ask DWM to round a top-level window. `small` selects the tighter
    radius Win11 uses for tooltips/flyout-adjacent surfaces."""
    try:
        pref = ctypes.c_int(DWMWCP_ROUNDSMALL if small else DWMWCP_ROUND)
        dwmapi.DwmSetWindowAttribute(ctypes.c_void_p(hwnd), DWMWA_WINDOW_CORNER_PREFERENCE,
                                     ctypes.byref(pref), ctypes.sizeof(pref))
    except OSError:
        pass  # pre-Win11: square corners, no harm


def set_dialog_frame(hwnd) -> None:
    """Fixed-dialog chrome for a titled Tk toplevel: keep the standard close
    button, drop minimize/maximize, and give the window its own taskbar
    button.

    Tk toplevels are OWNED windows (owner = the hidden root), and owned
    windows get no taskbar button; WS_EX_APPWINDOW forces one. The shell only
    re-reads that bit on a hide/show cycle, so callers apply this while the
    window is invisible and then (re)map it. Best-effort: on failure the
    stock frame remains, which is fully functional (close button included)."""
    try:
        style = _GetWindowLongPtrW(ctypes.c_void_p(hwnd), GWL_STYLE)
        _SetWindowLongPtrW(ctypes.c_void_p(hwnd), GWL_STYLE,
                           style & ~(WS_MINIMIZEBOX | WS_MAXIMIZEBOX))
        ex_style = _GetWindowLongPtrW(ctypes.c_void_p(hwnd), GWL_EXSTYLE)
        _SetWindowLongPtrW(ctypes.c_void_p(hwnd), GWL_EXSTYLE,
                           ex_style | WS_EX_APPWINDOW)
        user32.SetWindowPos(ctypes.c_void_p(hwnd), None, 0, 0, 0, 0,
                            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER
                            | SWP_NOACTIVATE | SWP_FRAMECHANGED)
    except OSError:
        pass


def set_titlebar_dark(hwnd, enabled: bool) -> None:
    """Ask DWM for a dark (or light) title bar on a decorated window.
    Attribute 20 first (documented, Win11 / Win10 20H1+), then 19 for
    1809-1909. Best-effort: older systems keep a light title bar."""
    value = ctypes.c_int(1 if enabled else 0)
    for attr in (DWMWA_USE_IMMERSIVE_DARK_MODE,
                 DWMWA_USE_IMMERSIVE_DARK_MODE_PRE20H1):
        try:
            if dwmapi.DwmSetWindowAttribute(ctypes.c_void_p(hwnd), attr,
                                            ctypes.byref(value),
                                            ctypes.sizeof(value)) == 0:
                return
        except OSError:
            return


def toplevel_hwnd(top) -> int:
    """Native HWND for a Tk toplevel. Tk's winfo_id is the client child; for
    an overrideredirect window GetParent yields the actual top-level frame
    (falling back to the child when there is none)."""
    child = top.winfo_id()
    return user32.GetParent(child) or child


def show_error(message: str) -> None:
    log.error(message)
    try:
        user32.MessageBoxW(None, message, APP_NAME, MB_ICONERROR)
    except OSError:
        pass


# ---------------------------------------------------------------------------
# Small helpers
# ---------------------------------------------------------------------------

#: Exactly '#' + six hex digits. fullmatch (not int(x, 16)) is deliberate:
#: Python's int parsing also accepts '_', '+', '-' and surrounding
#: whitespace, so e.g. "#1_2345" would slip through a length+int check and
#: later crash the icon renderer during startup, outside any error barrier.
_HEX_COLOR = re.compile(r"#[0-9A-Fa-f]{6}")


def normalize_hex(value: "str | None") -> str:
    """Strict #RRGGBB validation; anything else collapses to white."""
    if not isinstance(value, str):
        return "#FFFFFF"
    value = value.strip()
    if not value.startswith("#"):
        value = "#" + value
    if _HEX_COLOR.fullmatch(value) is None:
        return "#FFFFFF"
    return value.upper()


def resource_path(name: str) -> "Path | None":
    """First existing candidate for a bundled data file (holiday_data.json,
    app.ico, ...), or None when the file is nowhere to be found.

    Frozen builds look in the PyInstaller bundle first (sys._MEIPASS covers
    both --onefile and --onedir), then next to the .exe so a loose file can
    override/supplement the bundle. Source runs look next to this module.
    """
    candidates: list[Path] = []
    if getattr(sys, "frozen", False):
        meipass = getattr(sys, "_MEIPASS", None)
        if meipass:
            candidates.append(Path(meipass) / name)
        candidates.append(Path(sys.executable).resolve().parent / name)
    else:
        candidates.append(Path(__file__).resolve().parent / name)
    return next((p for p in candidates if p.exists()), None)


class SingleInstance:
    """Named-mutex single-instance guard. Unlike a localhost port bind, it
    cannot collide with unrelated software and has no network surface."""

    def __init__(self, name: str = MUTEX_NAME) -> None:
        self._name = name
        self._handle = None

    def acquire(self) -> bool:
        self._handle = kernel32.CreateMutexW(None, False, self._name)
        if not self._handle:
            log.warning("CreateMutexW failed (err=%d); continuing without guard",
                        ctypes.get_last_error())
            return True  # fail open: a broken guard must not brick the app
        if ctypes.get_last_error() == ERROR_ALREADY_EXISTS:
            self.release()
            return False
        return True

    def release(self) -> None:
        if self._handle:
            kernel32.CloseHandle(self._handle)
            self._handle = None

class QuitSignal:
    """Named auto-reset event used to hand control to a newly launched copy.

    The launcher calls signal() to wake the incumbent; the incumbent runs
    host(callback) on a daemon thread that fires `callback` (a graceful
    shutdown) when woken. Keyed on a name -- not a PID or image name -- so it
    behaves identically whether the process is python.exe under VS Code or the
    frozen .exe, and it never reaches into another process the way an external
    kill would (which is exactly what endpoint protection flags).

    Invariant: the incumbent both holds the event handle AND waits on it (see
    host()/_wait). If a future change ever holds the handle without an active
    waiter, a stale auto-reset signal would survive and shut down the next
    instance at startup -- keep the hold and the wait together."""

    def __init__(self, name: str = QUIT_EVENT_NAME) -> None:
        self._name = name
        self._handle = None
        self._thread: threading.Thread | None = None

    def signal(self) -> bool:
        """Wake a running instance. CreateEventW opens the existing named
        event (or creates it if we got here first); SetEvent stays latched on
        an auto-reset event until a waiter consumes it, so the signal is never
        lost to a race with the incumbent's host() call."""
        handle = kernel32.CreateEventW(None, False, False, self._name)
        if not handle:
            log.warning("CreateEventW (signal) failed (err=%d)", ctypes.get_last_error())
            return False
        try:
            return bool(kernel32.SetEvent(handle))
        finally:
            kernel32.CloseHandle(handle)

    def host(self, on_quit) -> None:
        """Incumbent side: block on the event off-thread, run on_quit when set."""
        self._handle = kernel32.CreateEventW(None, False, False, self._name)
        if not self._handle:
            log.warning("CreateEventW (host) failed (err=%d); eviction disabled",
                        ctypes.get_last_error())
            return
        self._thread = threading.Thread(
            target=self._wait, args=(on_quit,), name="weeknumber-evict", daemon=True)
        self._thread.start()

    def _wait(self, on_quit) -> None:
        if kernel32.WaitForSingleObject(self._handle, INFINITE) == WAIT_OBJECT_0:
            log.info("Eviction requested by a new instance; shutting down")
            try:
                on_quit()
            except Exception:
                log.exception("Eviction shutdown callback failed")

    def release(self) -> None:
        # Wake our own waiter so the daemon thread unblocks and ends, then close.
        if self._handle:
            kernel32.SetEvent(self._handle)
            kernel32.CloseHandle(self._handle)
            self._handle = None

class WeekNumber:
    def __init__(self) -> None:
        self.refresh()

    def refresh(self) -> None:
        self.last_updated = dt.datetime.now()
        self.number: int = self.last_updated.isocalendar()[1]

# ---------------------------------------------------------------------------
# Settings
# ---------------------------------------------------------------------------
class SettingsStore:
    """JSON settings. Startup state is intentionally NOT stored here -- the
    Run registry key is the single source of truth, so the menu checkbox can
    never desync from reality.

    The file lives in a user-writable directory and is therefore treated as
    untrusted input: size-capped before parsing and type-validated per key."""

    _DEFAULTS = {
        "SelectedColor": "#FFFFFF",
        "IsBold": True,
        "HolidayCountry": None,
        "DarkMode": False,
    }

    def __init__(self) -> None:
        self._data = dict(self._DEFAULTS)
        self.load()

    def load(self) -> None:
        try:
            if SETTINGS_FILE.stat().st_size > MAX_SETTINGS_BYTES:
                log.warning("Settings file exceeds %d bytes; ignoring it", MAX_SETTINGS_BYTES)
                return
            raw = json.loads(SETTINGS_FILE.read_text(encoding="utf-8"))
        except FileNotFoundError:
            return
        except Exception:
            log.exception("Failed to load settings; using defaults")
            return
        if not isinstance(raw, dict):
            log.warning("Settings root is not an object; using defaults")
            return
        color = raw.get("SelectedColor")
        if isinstance(color, str):
            self._data["SelectedColor"] = normalize_hex(color)
        bold = raw.get("IsBold")
        if isinstance(bold, bool):
            self._data["IsBold"] = bold
        country = raw.get("HolidayCountry")
        if country is None or isinstance(country, str):
            self._data["HolidayCountry"] = country
        dark_mode = raw.get("DarkMode")
        if isinstance(dark_mode, bool):
            self._data["DarkMode"] = dark_mode

    def save(self) -> None:
        try:
            SETTINGS_DIR.mkdir(parents=True, exist_ok=True)
            fd, tmp_name = tempfile.mkstemp(prefix="settings-", suffix=".json.tmp", dir=str(SETTINGS_DIR))
            tmp = Path(tmp_name)
            try:
                with os.fdopen(fd, "w", encoding="utf-8") as f:
                    json.dump(self._data, f, indent=2)
                os.replace(tmp, SETTINGS_FILE)  # atomic on NTFS
            finally:
                try:
                    if tmp.exists():
                        tmp.unlink()
                except OSError:
                    pass
        except Exception:
            log.exception("Failed to save settings")

    @property
    def selected_color(self) -> str:
        return normalize_hex(self._data.get("SelectedColor"))

    @selected_color.setter
    def selected_color(self, value: str) -> None:
        self._data["SelectedColor"] = normalize_hex(value)

    @property
    def is_bold(self) -> bool:
        return bool(self._data.get("IsBold", True))

    @is_bold.setter
    def is_bold(self, value: bool) -> None:
        self._data["IsBold"] = bool(value)

    @property
    def holiday_country(self) -> "str | None":
        value = self._data.get("HolidayCountry")
        return value if isinstance(value, str) else None

    @holiday_country.setter
    def holiday_country(self, value: "str | None") -> None:
        self._data["HolidayCountry"] = value if isinstance(value, str) else None

    @property
    def is_dark_mode(self) -> bool:
        return bool(self._data.get("DarkMode", False))

    @is_dark_mode.setter
    def is_dark_mode(self, value: bool) -> None:
        self._data["DarkMode"] = bool(value)


    def validate_holiday_country(self, valid_codes: "set[str] | list[str] | tuple[str, ...]") -> None:
        """Clear a stale saved holiday country code.

        Holiday data is bundled data, not user input. If a country code was
        removed or renamed between versions, keeping the old string leaves the
        radio menu with no valid selected item. Treat that as "Off".
        """
        value = self.holiday_country
        if value is not None and value not in set(valid_codes):
            self._data["HolidayCountry"] = None
            self.save()

# ---------------------------------------------------------------------------
# Holiday data
# ---------------------------------------------------------------------------
# Offline public-holiday tables. Loaded from non-executable JSON only.
_HOLIDAY_TABLE: dict[str, dict[int, dict[str, str]]] = {}
HOLIDAY_COUNTRIES: list[tuple[str, str]] = []

def load_holiday_data() -> None:
    """Load holiday tables from non-executable JSON only.

    The file is resolved via resource_path(), so PyInstaller bundles
    (sys._MEIPASS) and a loose holiday_data.json next to the .exe/script are
    both supported.

    JSON object keys are strings, so year keys are normalized back to int here;
    this keeps holiday_name() compatible with the original Python-data layout.
    """
    try:
        path = resource_path("holiday_data.json")
        if path is None:
            log.info("holiday_data.json not found; holiday support disabled")
            return
        if path.stat().st_size > 2 * 1024 * 1024:
            log.warning("holiday_data.json too large; ignoring")
            return

        raw = json.loads(path.read_text(encoding="utf-8"))
        holidays = raw.get("HOLIDAYS") if isinstance(raw, dict) else None
        countries = raw.get("COUNTRIES") if isinstance(raw, dict) else None
        if not isinstance(holidays, dict) or not isinstance(countries, list):
            log.warning("holiday_data.json has invalid schema; ignoring")
            return

        safe_countries: list[tuple[str, str]] = []
        for item in countries:
            if (isinstance(item, list) and len(item) == 2
                    and isinstance(item[0], str) and isinstance(item[1], str)):
                safe_countries.append((item[0][:16], item[1][:80]))

        safe_holidays: dict[str, dict[int, dict[str, str]]] = {}
        for code, years in holidays.items():
            if not isinstance(code, str) or not isinstance(years, dict):
                continue
            code = code[:16]
            safe_holidays[code] = {}
            for year_key, days in years.items():
                try:
                    year = int(year_key)
                except (TypeError, ValueError):
                    continue
                if not isinstance(days, dict):
                    continue
                safe_holidays[code][year] = {
                    str(date)[:10]: str(name)[:120]
                    for date, name in days.items()
                    if isinstance(date, str) and isinstance(name, str)
                }

        _HOLIDAY_TABLE.clear()
        _HOLIDAY_TABLE.update(safe_holidays)
        HOLIDAY_COUNTRIES.clear()
        HOLIDAY_COUNTRIES.extend(safe_countries)
        log.info("Loaded holiday data from %s (%d countries)", path, len(HOLIDAY_COUNTRIES))
    except Exception:
        log.exception("Failed to load holiday_data.json; holiday support disabled")

def holiday_name(country: "str | None", on_date: dt.date) -> "str | None":
    """Localized public-holiday name for a date, or None. Pure dict lookups --
    no per-call computation -- so it is cheap to run for every visible day."""
    if not country:
        return None
    years = _HOLIDAY_TABLE.get(country, {})
    return (years.get(on_date.year) or years.get(str(on_date.year)) or {}).get(on_date.isoformat())

# ---------------------------------------------------------------------------
# Run-at-startup registry handling
# ---------------------------------------------------------------------------
def _quote_cmd_arg(path: Path) -> str:
    return '"' + str(path).replace('"', '') + '"'

def _resolved_launch_command() -> str | None:
    """Resolved, quoted command for the Run value.

    In source mode, register the Python interpreter plus the script. Registering
    only the .py file relies on file associations and is easier to hijack.
    """
    try:
        if getattr(sys, "frozen", False):
            exe = Path(sys.executable).resolve(strict=True)
            return _quote_cmd_arg(exe)
        py = Path(sys.executable).resolve(strict=True)
        script = Path(sys.argv[0]).resolve(strict=True)
        return f"{_quote_cmd_arg(py)} {_quote_cmd_arg(script)}"
    except OSError:
        return None

def _startup_location_is_high_risk() -> bool:
    """Refuse autostart from common mutable/drop locations (Temp, Downloads,
    Desktop, Public). Best-effort guardrail, not a substitute for signing and
    installing to an ACL-protected directory.
    """
    try:
        launch = Path(sys.executable if getattr(sys, "frozen", False) else sys.argv[0]).resolve(strict=True)
    except OSError:
        return True  # cannot resolve our own path -> treat as unsafe

    candidates: list[Path] = [Path(tempfile.gettempdir())]
    for fid in (FOLDERID_DOWNLOADS, FOLDERID_DESKTOP, FOLDERID_PUBLIC):
        kf = known_folder_path(fid)
        if kf is not None:
            candidates.append(kf)
    # Literal fallbacks in case a shell lookup failed.
    candidates += [Path.home() / "Downloads", Path.home() / "Desktop"]

    launch_s = str(launch).casefold()
    sep = os.sep.casefold()
    seen: set[str] = set()
    for c in candidates:
        try:
            root = str(c.resolve()).casefold()
        except OSError:
            continue
        if root in seen:
            continue
        seen.add(root)
        if launch_s == root or launch_s.startswith(root + sep):
            return True
    return False

def is_startup_disabled_by_user() -> bool:
    """True when Task Manager / Settings flagged the entry as Disabled."""
    try:
        with winreg.OpenKey(HKCU, STARTUP_APPROVED_KEY) as key:
            value, regtype = winreg.QueryValueEx(key, STARTUP_APP_NAME)
        return regtype == winreg.REG_BINARY and len(value) > 0 and value[0] == 0x03
    except OSError:
        return False

def _approve_startup() -> None:
    """Mark the entry enabled in StartupApproved. Called ONLY on an explicit
    user enable -- never on launch -- so a Task Manager 'Disabled' choice is
    never silently overridden."""
    try:
        with winreg.CreateKeyEx(HKCU, STARTUP_APPROVED_KEY, 0, winreg.KEY_SET_VALUE) as key:
            winreg.SetValueEx(key, STARTUP_APP_NAME, 0, winreg.REG_BINARY, APPROVED_ENABLED_VALUE)
    except OSError:
        log.exception("Failed to write StartupApproved")

def set_startup(enable: bool) -> bool:
    """Returns success so the caller can surface failures instead of leaving
    the menu checkbox lying."""
    try:
        with winreg.CreateKeyEx(HKCU, RUN_KEY, 0, winreg.KEY_SET_VALUE) as key:
            if not enable:
                try:
                    winreg.DeleteValue(key, STARTUP_APP_NAME)
                except FileNotFoundError:
                    pass
                return True
            if _startup_location_is_high_risk():
                log.error("Refusing to register startup from a high-risk writable location")
                return False
            path = _resolved_launch_command()
            if path is None:
                log.error("Cannot resolve launch path; startup not registered")
                return False
            winreg.SetValueEx(key, STARTUP_APP_NAME, 0, winreg.REG_SZ, path)
        _approve_startup()
        return True
    except OSError:
        log.exception("Failed to update Run key")
        return False

def is_startup_enabled() -> bool:
    try:
        with winreg.OpenKey(HKCU, RUN_KEY) as key:
            winreg.QueryValueEx(key, STARTUP_APP_NAME)
    except OSError:
        return False
    return not is_startup_disabled_by_user()

def refresh_startup_path() -> None:
    """If registered and the binary has moved (or the value was tampered into
    a non-REG_SZ type), repoint the Run value to the current resolved path.
    Deliberately does NOT touch StartupApproved: launching the app must not
    re-enable an entry the user disabled."""
    try:
        with winreg.OpenKey(HKCU, RUN_KEY, 0, winreg.KEY_READ | winreg.KEY_SET_VALUE) as key:
            try:
                current, regtype = winreg.QueryValueEx(key, STARTUP_APP_NAME)
            except FileNotFoundError:
                return
            new = _resolved_launch_command()
            if new is None:
                return
            stale = (regtype != winreg.REG_SZ
                     or not isinstance(current, str)
                     or current.lower() != new.lower())
            if stale:
                if _startup_location_is_high_risk():
                    # Same policy as set_startup(): never repoint autostart into
                    # a writable drop location. Leave the existing entry intact.
                    log.warning("Refusing to repoint startup entry to a high-risk location")
                    return
                winreg.SetValueEx(key, STARTUP_APP_NAME, 0, winreg.REG_SZ, new)
                log.info("Startup path updated to %s", new)
    except OSError:
        log.exception("refresh_startup_path failed")
