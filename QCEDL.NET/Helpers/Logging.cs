namespace Qualcomm.EmergencyDownload.Helpers;

public enum LogLevel
{
    Trace,
    Debug,
    Info,
    Warning,
    Error
}

public static class Logging
{
    private static readonly Lock LockObj = new();
    public static LogLevel CurrentLogLevel { get; set; } = LogLevel.Info;

    /// <summary>
    /// Fired for every log line that passes the current level filter. The GUI subscribes
    /// to route logs into its live log view; the console sink below is always active so
    /// running the GUI from a terminal still surfaces output.
    /// </summary>
    public static event Action<DateTime, LogLevel, string?>? LogEmitted;

    /// <summary>When false, the console sink is suppressed (useful for GUI-only hosts).</summary>
    public static bool ConsoleSinkEnabled { get; set; } = true;

    public static void Log(string? message, LogLevel level = LogLevel.Info)
    {
        if (level < CurrentLogLevel)
        {
            return;
        }
        var timestamp = DateTime.Now;
        lock (LockObj)
        {
            if (ConsoleSinkEnabled)
            {
                var originalColor = Console.ForegroundColor;
                var prefix = level switch
                {
                    LogLevel.Trace => "[TRACE] ",
                    LogLevel.Debug => "[DEBUG] ",
                    LogLevel.Info => "[INFO]  ",
                    LogLevel.Warning => "[WARN]  ",
                    LogLevel.Error => "[ERROR] ",
                    _ => "[INFO]  ",
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
                Console.WriteLine($"{timestamp:HH:mm:ss.fff} {prefix}{message}");
                Console.ForegroundColor = originalColor;
            }
        }

        try
        {
            LogEmitted?.Invoke(timestamp, level, message);
        }
        catch
        {
            // Sinks must never break the caller.
        }
    }

    public static void ShowProgress(long current, long total, DateTime _)
    {
        var percentage = total == 0 ? 100.0 : current * 100.0 / total;
        Log($"Progress: {percentage:F2}%");
    }
}