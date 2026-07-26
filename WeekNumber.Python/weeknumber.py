"""WeekNumber launcher and application lifecycle."""

import atexit
import time
import tkinter as tk

from weeknumber_core import (
    APP_NAME,
    APP_VERSION,
    HOLIDAY_COUNTRIES,
    QuitSignal,
    SettingsStore,
    SingleInstance,
    enable_dpi_awareness,
    load_holiday_data,
    log,
    refresh_startup_path,
    setup_logging,
    show_error,
    system_dpi,
)
from weeknumber_tray import TrayIcon


class Application:
    REFRESH_MS = 60_000
    #: Eviction handover budget -- how long a launch waits for the incumbent
    #: to drop the mutex before giving up.
    HANDOVER_TIMEOUT_S = 5.0
    HANDOVER_POLL_S = 0.1

    def __init__(self) -> None:
        self.single_instance = SingleInstance()
        self.quit_signal = QuitSignal()
        self.settings = SettingsStore()
        self.settings.validate_holiday_country(
            {code for code, _name in HOLIDAY_COUNTRIES}
        )
        self.tray = TrayIcon(self)
        self.root: tk.Tk | None = None
        self.running = True

    def run_on_ui(self, func) -> None:
        """Marshal a callable onto the Tk main thread. `after` from a foreign
        thread is the established pystray<->Tk pattern; every Tk object is
        then only ever touched on the main thread."""
        if self.root is not None:
            try:
                self.root.after(0, func)
            except (tk.TclError, RuntimeError):
                pass

    def request_shutdown(self) -> None:
        self.run_on_ui(self._shutdown)

    def _shutdown(self) -> None:
        if not self.running:
            return
        self.running = False
        self.tray.stop()
        if self.root is not None:
            self.root.quit()

    def _periodic_refresh(self) -> None:
        if not self.running or self.root is None:
            return
        try:
            self.tray.week.refresh()
            self.tray.refresh()
        except Exception:
            log.exception("Periodic refresh failed")
        self.root.after(self.REFRESH_MS, self._periodic_refresh)

    def _claim_singleton(self) -> bool:
        """Take the single-instance mutex, evicting any incumbent first.

        Launching the app -- from VS Code or the .exe -- replaces the running
        copy: signal it to quit, then poll until it releases the mutex and we
        can take it. No external process kill: the incumbent tears its own
        tray/Tk down cleanly, which also avoids the leftover ghost tray icon a
        forced TerminateProcess would leave behind."""
        if self.single_instance.acquire():
            return True
        log.info("Existing instance detected; requesting handover")
        self.quit_signal.signal()
        deadline = time.monotonic() + self.HANDOVER_TIMEOUT_S
        while time.monotonic() < deadline:
            time.sleep(self.HANDOVER_POLL_S)
            if self.single_instance.acquire():
                log.info("Handover complete; this instance is now primary")
                return True
        show_error(f"{APP_NAME} is already running and did not respond in time. "
                   f"End it from Task Manager, then relaunch.")
        return False

    def run(self) -> int:
        if not self._claim_singleton():
            return 0
        atexit.register(self.single_instance.release)
        atexit.register(self.quit_signal.release)

        refresh_startup_path()

        self.root = tk.Tk()
        self.root.withdraw()
        self.root.protocol("WM_DELETE_WINDOW", lambda: None)
        # Incumbent side of the handover. Started once root exists so the
        # wake callback can marshal a shutdown onto the Tk thread.
        self.quit_signal.host(self.request_shutdown)

        self.tray.run()
        self.root.after(self.REFRESH_MS, self._periodic_refresh)
        log.info("%s %s started (tray icon %dpx, system DPI %d)",
                 APP_NAME, APP_VERSION, self.tray.icon_size, system_dpi())
        try:
            self.root.mainloop()
        finally:
            self.tray.stop()  # idempotent; covers abnormal mainloop exits
            try:
                self.root.destroy()
            except tk.TclError:
                pass
            self.quit_signal.release()
            self.single_instance.release()
        return 0



def main() -> int:
    setup_logging()
    load_holiday_data()
    enable_dpi_awareness()  # before Tk and before any DPI query
    try:
        return Application().run()
    except Exception:
        log.exception("Fatal error")
        show_error(f"{APP_NAME} encountered an unexpected error and needs to close. "
                   f"Details were written to the log file.")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
