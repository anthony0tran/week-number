namespace WeekNumber;

public class ExecutablePathProvider : IExecutablePathProvider
{
    public string GetExecutablePath()
    {
        return Application.ExecutablePath;
    }
}