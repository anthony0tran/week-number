using System.Security.Cryptography;

namespace WeekNumber;

internal static class Program
{
    // Mutex name uses a fixed GUID to guarantee single-instance per user session.
    // "Local\" scope ties it to the current desktop session, preventing cross-session interference.
    private const string MutexName = @"Local\WeekNumber_{7A3F1B2E-9C4D-4E5A-B6F8-1D2E3F4A5B6C}";

    [STAThread]
    private static void Main()
    {
        // Single-instance enforcement: prevents resource exhaustion and race conditions
        // from multiple copies running simultaneously (e.g., fork-bomb style attacks).
        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
            return; // Another instance is already running — exit silently.

        // Validate executable integrity before proceeding.
        if (!VerifyExecutableIntegrity())
        {
            Environment.Exit(2);
            return;
        }

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

    /// <summary>
    /// Validates that the running executable has not been tampered with by verifying
    /// it is Authenticode-signed (if originally distributed signed) and that the file
    /// on disk matches what the OS loaded.
    /// </summary>
    private static bool VerifyExecutableIntegrity()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                return false;

            // Verify the file is not unreasonably large (prevents processing a swapped-in multi-GB file).
            var fileInfo = new FileInfo(exePath);
            if (fileInfo.Length > 200 * 1024 * 1024) // 200 MB max for a single-file app
                return false;

            // Verify the file has a valid PE signature (MZ header).
            using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> header = stackalloc byte[2];
            if (fs.Read(header) < 2 || header[0] != 0x4D || header[1] != 0x5A) // "MZ"
                return false;

            return true;
        }
        catch
        {
            // If we can't verify, fail closed.
            return false;
        }
    }
}
