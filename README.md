# Week Number 📅

Never lose track of the current week again. 🗓️

Week Number is a tiny Windows tray app that keeps the current week number visible right in your taskbar corner. No opening apps, no digging through calendars, no extra clutter. 🎯

Perfect for people who work with schedules, planning cycles, school timetables, sprint boards, payroll weeks, or recurring deadlines. ✅

## Install from EXE 📥

1. Download the latest `WeekNumber.exe` from the official GitHub Releases page. 🔽
2. Double-click the downloaded EXE to launch the app. ▶️
3. If Microsoft Defender SmartScreen appears:
  - Click **More info**
  - Click **Run anyway**
4. The app will start in your system tray (notification area). ✅

## Why People Like It 💖

- **Always visible**: the week number lives in your system tray. 👀
- **One-click calendar**: left-click to open a clean popup calendar with week numbers. 🖱️
- **Your style**: choose your icon color and font style. 🎨
- **Set and forget**: optional startup mode launches it when Windows starts. 🚀
- **Lightweight**: built to stay out of your way. 🪶

## Features ✨

- Tray icon displays the current ISO week number. 🔢
- Left-click refreshes the icon and opens a modern popup calendar. 📆
- Right-click context menu includes:
  - Run at Startup ⚙️
  - Change Color 🎨
  - Font Style (Bold, Italic, Strikeout) ✍️
  - About ℹ️
  - Exit ❌
- Saves your selected color and font style automatically. 💾
- About dialog shows the app version and a GitHub link. 🔗

## Quick Start 🚀

1. Launch Week Number. 🖥️
2. Look at your notification area (system tray) for the week-number icon. 🔍
3. Left-click to open the calendar. 📅
4. Right-click for settings and options. ⚙️

## Built For Everyday Use 🛠️

- Uses ISO-8601 week numbering for consistent week values. 📏
- Updates cleanly after resume and on manual refresh. 🔄
- Designed for Windows with DPI-aware UI behavior. 🖼️

## Requirements 🖥️

- Windows 10 or newer 🪟

## For Developers 👩‍💻👨‍💻

Run from source:

```powershell
dotnet run --project .\WeekNumber\WeekNumber.csproj
```

Run tests:

```powershell
dotnet test .\WeekNumber.Tests\WeekNumber.Tests.csproj
```

Publish (single-file, self-contained `win-x64`):

```powershell
dotnet publish .\WeekNumber\WeekNumber.csproj -c Release
```

## Technical Notes 📝

- `Program` initializes WinForms, wires global exception handlers, and starts the tray lifecycle. 🔧
- `NotificationAreaIcon` owns the `NotifyIcon`, context menu actions, tooltip text, and event handling. 🛎️
- `WeekNumber` stores current number + last updated timestamp and recalculates on refresh. 🔢
- `IconFactory` renders the week number into a transparent tray icon. 🎨
- `CalendarForm` hosts `CalendarControl`, a DPI-aware custom calendar with ISO week numbers. 📅
- `StartupHelper` manages startup registration in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. 🛠️

## Security and Reliability Notes 🔒

- Startup registration is only written for trusted executable locations. ✅
- P/Invoke DLL search paths are constrained to System32. 🗂️
- The app runs as standard user (`asInvoker`) and includes DPI-awareness manifest settings. 🖼️
- Unhandled exceptions are caught and displayed as a minimal, non-sensitive error dialog. ⚠️

## License 📜

MIT License. See `LICENSE` for details.
