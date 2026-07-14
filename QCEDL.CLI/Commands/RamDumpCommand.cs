using System.CommandLine;
using System.Diagnostics;
using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;
using Qualcomm.EmergencyDownload.Layers.PBL.Sahara;

namespace QCEDL.CLI.Commands;

internal sealed class RamDumpCommand
{
    private const int ProgressUpdateIntervalMilliseconds = 100;

    private static readonly Option<DirectoryInfo> OutputOption = new(
        aliases: ["--output", "-o"],
        description: "Directory where ramdump region files will be stored.",
        getDefaultValue: () => new(Environment.CurrentDirectory));

    private static readonly Argument<string?> SegmentFilterArgument = new(
        "segment-filter",
        "Optional comma-separated, case-sensitive glob patterns selecting region file names.")
    {
        Arity = ArgumentArity.ZeroOrOne
    };

    public static Command Create(GlobalOptionsBinder globalOptionsBinder)
    {
        var command = new Command(
            "ramdump",
            "Collects a 64-bit Sahara memory dump from a Qualcomm crashdump device (normally PID 0x900E).")
        {
            OutputOption,
            SegmentFilterArgument
        };

        command.SetHandler(
            ExecuteAsync,
            globalOptionsBinder,
            OutputOption,
            SegmentFilterArgument);

        return command;
    }

    private static async Task<int> ExecuteAsync(
        GlobalOptionsBinder globalOptions,
        DirectoryInfo outputDirectory,
        string? segmentFilter)
    {
        Logging.Log(
            $"Executing 'ramdump' command: Output '{outputDirectory.FullName}', Filter '{segmentFilter ?? "<all>"}'...",
            LogLevel.Trace);

        return await CommandExecutor.RunAsync("ramdump", async () =>
        {
            using var manager = new EdlManager(globalOptions);
            var stopwatch = new Stopwatch();
            var progressUpdateStopwatch = Stopwatch.StartNew();
            ProgressReporter? reporter = null;
            string? activeFileName = null;

            void ReportProgress(QualcommSaharaRamDumpProgress progress)
            {
                var regionChanged = !string.Equals(
                    activeFileName,
                    progress.Region.FileName,
                    StringComparison.Ordinal);
                if (regionChanged)
                {
                    if (activeFileName is not null)
                    {
                        Console.WriteLine();
                    }

                    activeFileName = progress.Region.FileName;
                    stopwatch.Restart();
                    reporter = new(stopwatch, $"Dumping {activeFileName}");
                    Logging.Log(
                        $"Dumping region '{progress.Region.RegionName}' to '{activeFileName}' " +
                        $"(address 0x{progress.Region.Address:X}, length 0x{progress.Region.Length:X})...",
                        LogLevel.Debug);
                }

                var regionCompleted = progress.BytesCompleted == progress.TotalBytes;
                if (!regionChanged && !regionCompleted &&
                    progressUpdateStopwatch.ElapsedMilliseconds < ProgressUpdateIntervalMilliseconds)
                {
                    return;
                }

                reporter!.Report(ToProgressValue(progress.BytesCompleted), ToProgressValue(progress.TotalBytes));
                progressUpdateStopwatch.Restart();
                if (regionCompleted)
                {
                    stopwatch.Stop();
                }
            }

            var regions = await manager.CollectRamDumpAsync(
                outputDirectory.FullName,
                segmentFilter,
                ReportProgress);

            if (activeFileName is not null)
            {
                Console.WriteLine();
            }

            if (regions.Count == 0)
            {
                Logging.Log("No ramdump regions matched the requested segment filter.", LogLevel.Warning);
            }
            else
            {
                var totalBytes = regions.Aggregate(0m, (total, region) => total + region.Length);
                Logging.Log(
                    $"Ramdump completed: {regions.Count} region(s), " +
                    $"{totalBytes / (1024m * 1024m):F2} MiB written to '{outputDirectory.FullName}'.");
            }

            return 0;
        });
    }

    private static long ToProgressValue(ulong value)
    {
        return value > long.MaxValue ? long.MaxValue : (long)value;
    }
}