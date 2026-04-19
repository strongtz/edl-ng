using System.Collections.ObjectModel;
using Avalonia.Threading;
using Qualcomm.EmergencyDownload.Helpers;

namespace QCEDL.GUI.Services;

/// <summary>
/// Observable log sink that forwards <see cref="Logging"/> events to the UI thread so the
/// Logs view can bind directly to <see cref="Entries"/>. Capped to avoid unbounded growth.
/// </summary>
public sealed class ObservableLogSink
{
    private const int MaxEntries = 2000;

    public ObservableCollection<LogEntry> Entries { get; } = [];

    public void Emit(DateTime timestamp, LogLevel level, string? message)
    {
        if (message is null)
        {
            return;
        }

        var entry = new LogEntry(timestamp, level, message);
        Dispatcher.UIThread.Post(() =>
        {
            Entries.Add(entry);
            while (Entries.Count > MaxEntries)
            {
                Entries.RemoveAt(0);
            }
        });
    }

    public void Clear()
    {
        Dispatcher.UIThread.Post(Entries.Clear);
    }
}

public sealed record LogEntry(DateTime Timestamp, LogLevel Level, string Message)
{
    public string TimeText => Timestamp.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
    public string LevelText => Level switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Info => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERROR",
        _ => "INFO",
    };
}