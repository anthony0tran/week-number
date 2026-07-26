# WeekNumber 1.0

Windows tray app that shows the current ISO week number as the tray icon and
opens a two-month calendar flyout on click. Offline, single .exe, no
installer, no network I/O.

## What it does

- Tray icon renders the week number (color and bold configurable), tooltip
  shows the last refresh time. Refreshes every minute and on interaction.
- Left click opens a borderless two-month calendar next to the taskbar edge
  of the monitor the cursor is on, DPI-scaled per monitor, with DWM-rounded
  corners. It closes on focus loss, Esc, or a click elsewhere.
- Calendar: ISO week-number column, current-week band, today as a filled
  accent pill, clicked day as a soft fill with an accent ring, public
  holidays as pills with a name tooltip. Navigation: chevrons, mouse wheel,
  arrow keys; Home returns to the current month.
- Right click on a day copies `dd-mm-yyyy`; right click on a week number
  copies `yyyy CWww` (ISO week-year: 2021-01-01 copies `2020 CW53`).
- About window via the tray menu: app icon, name, version, GitHub button.
  A real titled window with the standard close button (no minimize/maximize),
  its own taskbar button while open, and a dark title bar in dark mode. It
  stays open until closed (close button, Alt+F4, or Esc). Centred on the
  monitor the cursor is on and sized by that monitor's DPI, so it has one
  physical size on every display. Single instance -- reopening focuses the
  open window.
- Tray menu: run at startup, bold, icon color, dark mode, holiday country,
  about, exit.

## How it works

Six modules, one direction of dependency:
`weeknumber.py` → `weeknumber_tray.py` → `weeknumber_about.py` →
`weeknumber_calendar.py` → `weeknumber_core.py` / `weeknumber_clipboard.py`.
(The tray also imports the calendar directly; every arrow still points the
same way.)

**weeknumber_core.py** — everything that talks to Windows, and no GUI
toolkit at all: ctypes bindings (DPI awareness, monitor geometry, DWM window
rounding and dark title bars, window-frame surgery for the About dialog,
taskbar edge detection), the settings store, holiday data loading, startup
registration, logging, and the single-instance primitives. It pins the DLL
search path to System32 before the first native call.

**weeknumber.py** — lifecycle. Claims the single-instance mutex; if another
copy holds it, it signals a named event and waits up to 5 s for the incumbent
to shut itself down and release, then takes over. This makes "launch again"
mean "replace the running copy" without killing a process (a killed tray
process leaves a ghost icon). It then builds a hidden Tk root, starts the
tray, and runs the Tk mainloop with a 60 s refresh timer.

**Threading model.** pystray runs the tray icon on its own thread; Tk is not
thread-safe. Every tray callback therefore marshals UI work to the Tk main
thread via `root.after(0, ...)` (`Application.run_on_ui`); nothing else ever
touches a Tk object off the main thread. All tray callbacks are wrapped by a
`tray_callback` decorator that logs exceptions instead of letting them kill
the pystray thread silently.

**weeknumber_tray.py** — icon rendering and menu. `IconRenderer` draws the
number at the exact physical small-icon size (queried from the system, so
125 %/150 % taskbars get 20/24 px, not 16): 8× supersampled, font size fitted
by measurement, then centred on the glyph's ink box, with bold condensed
≤15 % horizontally so it keeps regular's glyph height. `NativeSizeIcon`
bypasses pystray's ICO loading, which round-trips through the LARGE icon
size and arrives blurry; it writes a one-frame ICO at native size and loads
it 1:1, falling back to stock pystray on any failure.

**weeknumber_calendar.py** — the flyout. A `Toplevel` with
`overrideredirect`, positioned against the taskbar edge from core's monitor
geometry, rounded by DWM. All visual states (today, selected, holiday,
hover, week-band end caps, nav chevrons) are tiny supersampled PIL images
composed from shared masks and swapped on Tk labels; this is what makes
rounded pills and a continuous week band possible in Tk, which has no
native border-radius. Chevrons are drawn geometry, not font glyphs — Tk
centres text by font line box, which sits visibly above the ink of Segoe
UI's angle characters; one arm is drawn and the rest derived by exact bitmap
reflection, so centring is structural. If Pillow's ImageTk is unavailable
the calendar degrades to flat rectangular fills, fully functional. The
module also exports the shared styling base (`ThemedPopup`,
`ui_font_family`) used by the About window.

**weeknumber_about.py** — the About window. Unlike the calendar flyout it is
a real titled window: standard close button only (minimize/maximize are
stripped and a taskbar button is forced via core's `set_dialog_frame` --
Tk toplevels are owned windows, which normally get none), a DWM dark title
bar following the app theme, DPI-sized for the monitor it opens on and
centred in that monitor's work area. Single instance; it stays open until
closed. The card content (icon, version, rounded GitHub button) shares the
calendar's palette and pill construction, with the same ImageTk degradation.

**weeknumber_clipboard.py** — the single source of truth for the two copy
formats and the `yyyy CWww` string used anywhere in the UI.

**State and data.** Settings live in `%APPDATA%\WeekNumberPy\settings.json`
— written atomically (temp file + `os.replace`), read defensively: size cap,
type checks, strict color validation, unknown holiday countries dropped.
Holiday tables come from `holiday_data.json` next to the executable (or
bundled into it); size-capped and schema-checked, and its absence only
disables holiday display. A rotating size-capped log sits in the same
directory.

## Run from source

Windows 10+, Python 3.10+.

```
pip install pystray pillow
python weeknumber.py
```

## Build the .exe

One build file: `build_exe.bat`. It validates the environment, generates the
PyInstaller spec and the VERSIONINFO resource into `build\` (the version
number is read from `APP_VERSION` in `weeknumber_core.py` -- single source
of truth), builds, and verifies the output.

```
build_exe.bat                 one-file dist\WeekNumber.exe
build_exe.bat --onedir        folder build dist\WeekNumber\ (fastest logon start)
build_exe.bat --venv          build in an isolated pinned venv (smallest,
                              reproducible exe regardless of global packages)
build_exe.bat --nopause       non-interactive; returns exit code 0/1
```

Size posture: bytecode is built with `--optimize 2` (asserts and docstrings
stripped; the app uses neither at runtime), UPX is deliberately off (its size
win is not worth antivirus false positives -- a quarantined exe is lost
functionality), and Qt/scientific excludes guard against a polluted global
environment. `--venv` is the honest size lever. Expect roughly 11-15 MB for
onefile: tkinter, Pillow, and pystray are the payload, and pruning Pillow's
codec plugins below that risks runtime breakage for ~1 MB.

**Compatibility.** The exe runs on Windows 10 (1703 or later) and Windows 11,
x64, with no runtime dependencies; Windows 11 on ARM runs it via the built-in
x64 emulation. Windows 7/8.1 are out of scope: current Python itself requires
Windows 10+, and the app's per-monitor-v2 DPI handling needs 10 1703 anyway.
Rounded window corners appear on Windows 11 and degrade to square on 10.

For a machine where WeekNumber starts at every logon, prefer `--onedir`:
onefile re-extracts its binaries to %TEMP% on every launch (slower logon,
re-scanned by AV each time, and some corporate policies block executing from
%TEMP%). Onefile remains the convenient form for sharing.

## Deployment notes (they matter for the security posture)

1. Install to a directory standard users cannot write to (e.g.
   `C:\Program Files\WeekNumber`). The app hardens its own DLL search path,
   but the PyInstaller bootloader's loads run first; a write-protected
   directory closes that gap. The app deliberately refuses to register
   startup from Temp/Downloads/Desktop/Public.
2. Sign the binary if you distribute it; unsigned onefile PyInstaller
   executables are routinely flagged by SmartScreen.
3. Pin dependency versions for reproducible builds.
