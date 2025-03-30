namespace WeekNumber;

internal static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        
        using var weekNumberIcon = NotificationAreaIcon.Instance;
        
        Application.Run();
    }
}