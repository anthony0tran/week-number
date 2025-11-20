# Week Number

Week Number is a high-performance Windows utility that leverages [NotifyIcon](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon?view=windowsdesktop-9.0) to display the current ISO week number directly in your system tray. 
## Features

- \*\*Taskbar Notification\*\*: Shows the current ISO week number as an icon in the Windows notification area.
- \*\*Accurate Calculation\*\*: Uses [ISOWeek](https://learn.microsoft.com/en-us/dotnet/api/system.globalization.isoweek.getweekofyear?view=net-9.0#system-globalization-isoweek-getweekofyear(system-datetime)) for correct week numbers.
- \*\*Customizable Appearance\*\*: Change the icon color and font style via the context menu.
- \*\*Startup Option\*\*: Easily enable or disable running the app at Windows startup.
- \*\*Quick Calendar Access\*\*: Left-click the icon to view a calendar.

## How It Works

- The app calculates the ISO week number using the `WeekNumber` class.
- The icon is rendered dynamically with the current week number using the `IconFactory` and `IIconFactory` classes.
- The `NotificationAreaIcon` class manages the tray icon, context menu, and user interactions.
- The `StartupManager` class handles Windows startup registration via the registry.

## Usage

1. \*\*Run the application\*\*. The current week number appears in your notification area.
2. \*\*Right-click the icon\*\* to access options:
    - Change color
    - Change font style
    - Enable/disable startup
    - Exit the application
3. \*\*Left-click the icon\*\* to update the week number and open the calendar.

## Requirements

- Windows 10 or later

## License

MIT License. See `LICENSE` for details.
