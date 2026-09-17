namespace NasSync.Core;

/// <summary>
/// Simple file-based logger that writes to a log file in the application directory.
/// Thread-safe via lock. Log entries include timestamp, level, and source.
/// </summary>
public static class AppLogger
{
    private static readonly object Lock = new();
    private static string? _logPath;

    /// <summary>
    /// Initializes the logger. Creates the log file in the application's base directory.
    /// </summary>
    public static void Initialize()
    {
        try
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            _logPath = Path.Combine(appDir, "nassync.log");

            // Write header
            File.AppendAllText(_logPath,
                $"\n{'=',-60}\n" +
                $"NAS Cloud Sync — {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                $"OS: {Environment.OSVersion}\n" +
                $"Architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}\n" +
                $"AppDir: {appDir}\n" +
                $"{'=',-60}\n");
        }
        catch
        {
            // Logging is best-effort
        }
    }

    /// <summary>
    /// Writes an informational log entry.
    /// </summary>
    public static void Info(string source, string message)
    {
        Write("INFO", source, message);
    }

    /// <summary>
    /// Writes a warning log entry.
    /// </summary>
    public static void Warn(string source, string message)
    {
        Write("WARN", source, message);
    }

    /// <summary>
    /// Writes an error log entry.
    /// </summary>
    public static void Error(string source, string message)
    {
        Write("ERROR", source, message);
    }

    /// <summary>
    /// Writes an error log entry with exception details.
    /// </summary>
    public static void Error(string source, string message, Exception ex)
    {
        Write("ERROR", source, $"{message}\n  Exception: {ex.GetType().Name}: {ex.Message}\n  Stack: {ex.StackTrace}");
    }

    /// <summary>
    /// Gets the log file path.
    /// </summary>
    public static string? LogPath => _logPath;

    private static void Write(string level, string source, string message)
    {
        if (_logPath is null) return;

        try
        {
            string entry = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] [{source}] {message}\n";

            lock (Lock)
            {
                File.AppendAllText(_logPath, entry);
            }

            // Also write to Debug output
            System.Diagnostics.Debug.Write(entry);
        }
        catch
        {
            // Logging is best-effort
        }
    }
}
