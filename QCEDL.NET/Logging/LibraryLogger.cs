using System.Runtime.CompilerServices;
namespace QCEDL.NET.Logging;

public enum LogLevel
{
    Trace,
    Debug,
    Info,
    Warning,
    Error
}
public static class LibraryLogger
{
    // Parameters: message, level, memberName, sourceFilePath, sourceLineNumber
    public static Action<string?, LogLevel, string?, string?, int?>? LogAction { get; set; }
    public static void Trace(string message,
        [CallerMemberName] string? memberName = null,
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        LogAction?.Invoke(message, LogLevel.Trace, memberName, sourceFilePath, sourceLineNumber);
    }
    public static void Debug(string message,
        [CallerMemberName] string? memberName = null,
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        LogAction?.Invoke(message, LogLevel.Debug, memberName, sourceFilePath, sourceLineNumber);
    }
    public static void Info(string message,
        [CallerMemberName] string? memberName = null,
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        LogAction?.Invoke(message, LogLevel.Info, memberName, sourceFilePath, sourceLineNumber);
    }
    public static void Warning(string message,
        [CallerMemberName] string? memberName = null,
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        LogAction?.Invoke(message, LogLevel.Warning, memberName, sourceFilePath, sourceLineNumber);
    }
    public static void Error(string? message,
        [CallerMemberName] string? memberName = null,
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = 0)
    {
        LogAction?.Invoke(message, LogLevel.Error, memberName, sourceFilePath, sourceLineNumber);
    }
}