namespace WeekNumber;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Suppress default WinForms crash dialogs that would expose full stack traces
        // and internal file paths to any observer.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => HandleFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                HandleFatal(ex);
        };

        ApplicationConfiguration.Initialize();
        using var weekNumberIcon = NotificationAreaIcon.Instance;
        Application.Run();
    }

    private static void HandleFatal(Exception ex)
    {
        // Show a minimal, non-revealing error message and exit cleanly.
        MessageBox.Show(
            "WeekNumber encountered an unexpected error and needs to close.",
            "WeekNumber",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);

        Environment.Exit(1);
    }
}
