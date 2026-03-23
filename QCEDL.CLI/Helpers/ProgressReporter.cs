using System.Diagnostics;

namespace QCEDL.CLI.Helpers;

internal sealed class ProgressReporter(Stopwatch stopwatch, string prefix)
{
    public long BytesReported { get; private set; }

    public void Report(long current, long total)
    {
        BytesReported = current;
        var percentage = total == 0 ? 100 : current * 100.0 / total;
        var elapsed = stopwatch.Elapsed;
        var speed = elapsed.TotalSeconds > 0 ? current / elapsed.TotalSeconds : 0;
        var speedStr = elapsed.TotalSeconds > 0.1 ? FormatSpeed(speed) : "N/A";
        Console.Write($"\r{prefix}: {percentage:F1}% ({current / (1024.0 * 1024.0):F2} / {total / (1024.0 * 1024.0):F2} MiB) [{speedStr}]      ");
    }

    public static string FormatSpeed(double bytesPerSecond)
    {
        return bytesPerSecond > 1024 * 1024 ? $"{bytesPerSecond / (1024 * 1024):F2} MiB/s" :
            bytesPerSecond > 1024 ? $"{bytesPerSecond / 1024:F2} KiB/s" :
            $"{bytesPerSecond:F0} B/s";
    }
}