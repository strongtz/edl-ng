namespace QCEDL.CLI.Helpers;

public enum LogLevel
{
    Trace,
    Debug,
    Info,
    Warning,
    Error
}

internal static class Logging
{
    private static readonly Lock LockObj = new();
    public static LogLevel CurrentLogLevel { get; set; } = LogLevel.Info;

    public static void Log(string? message, LogLevel level = LogLevel.Info)
    {
        if (level < CurrentLogLevel)
        {
            return;
        }
        lock (LockObj)
        {
            var originalColor = Console.ForegroundColor;
            var prefix = level switch
            {
                LogLevel.Trace => "[TRACE] ",
                LogLevel.Debug => "[DEBUG] ",
                LogLevel.Info => "[INFO]  ",
                LogLevel.Warning => "[WARN]  ",
                LogLevel.Error => "[ERROR] ",
                _ => "[INFO]  ", // Default, should not happen with enum
            };
            Console.ForegroundColor = level switch
            {
                LogLevel.Trace => ConsoleColor.DarkGray,
                LogLevel.Debug => ConsoleColor.Gray,
                LogLevel.Info => ConsoleColor.White,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Error => ConsoleColor.Red,
                _ => ConsoleColor.White,
            };
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {prefix}{message}");
            Console.ForegroundColor = originalColor;
        }
    }

    // TODO: Add progress bar functionality if needed later
    public static void ShowProgress(long current, long total, DateTime _)
    {
        // Placeholder
        var percentage = total == 0 ? 100.0 : current * 100.0 / total;
        Log($"Progress: {percentage:F2}%"); // Simple percentage for now
    }
}