using System.CommandLine;
using System.Diagnostics;
using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;
using Qualcomm.EmergencyDownload.Core;
using Qualcomm.EmergencyDownload.Helpers;

namespace QCEDL.CLI.Commands;

internal sealed class DumpRawprogramCommand
{
    private static readonly Argument<DirectoryInfo> DumpSaveDirArgument = new("dump_save_dir", "The directory to save partition files and rawprogram XML.") { Arity = ArgumentArity.ExactlyOne };

    private static readonly Option<uint> LunOption = new(
        aliases: ["--lun", "-u"],
        description: "Specify the LUN number to dump partitions from.",
        getDefaultValue: () => 0);

    private static readonly Option<bool> GenXmlOnlyOption = new(
        aliases: ["--gen-xml-only"],
        description: "Only generate rawprogram and patch XML files, do not dump partitions.",
        getDefaultValue: () => false);

    private static readonly Option<string[]> SkipOption = new(
        aliases: ["--skip"],
        description: "Skip dumping specified partitions (comma-separated). These partitions will still be included in the rawprogram XML.",
        getDefaultValue: () => []);

    public static Command Create(GlobalOptionsBinder globalOptionsBinder)
    {
        var command = new Command("dump-rawprogram", "Reads all partitions to individual files from a certain LUN and generates rawprogram XML file.")
        {
            DumpSaveDirArgument,
            LunOption,
            GenXmlOnlyOption,
            SkipOption
        };

        command.SetHandler(ExecuteAsync,
            globalOptionsBinder,
            DumpSaveDirArgument,
            LunOption,
            GenXmlOnlyOption,
            SkipOption);

        return command;
    }

    private static async Task<int> ExecuteAsync(
        EdlOptions globalOptions,
        DirectoryInfo dumpSaveDir,
        uint lun,
        bool genXmlOnly,
        string[] skipPartitions)
    {
        Logging.Log($"Executing 'dump-rawprogram' command: LUN {lun}, Save Directory '{dumpSaveDir.FullName}', GenXmlOnly={genXmlOnly}, SkipPartitions=[{string.Join(",", skipPartitions)}]...", LogLevel.Trace);

        var skipSet = new HashSet<string>(
            skipPartitions.SelectMany(p => p.Split(',')).Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)),
            StringComparer.OrdinalIgnoreCase);
        if (skipSet.Count > 0)
        {
            Logging.Log($"Will skip dumping partitions: {string.Join(", ", skipSet)}", LogLevel.Info);
        }

        return await CommandExecutor.RunAsync("dump-rawprogram", async () =>
        {
            using var manager = new EdlManager(globalOptions);

            string? lastName = null;
            Stopwatch? lastStopwatch = null;
            void Progress(DumpRawprogramRunner.DumpProgress p)
            {
                if (lastName != p.PartitionName)
                {
                    if (lastName != null)
                    {
                        Console.WriteLine();
                    }
                    lastName = p.PartitionName;
                    lastStopwatch = Stopwatch.StartNew();
                }
                var pct = p.BytesTotal == 0 ? 100 : p.BytesDone * 100.0 / p.BytesTotal;
                var speed = p.ElapsedSeconds > 0.1 ? p.BytesDone / p.ElapsedSeconds : 0;
                var speedStr = p.ElapsedSeconds > 0.1 ? ProgressReporter.FormatSpeed(speed) : "N/A";
                Console.Write($"\rDumping {p.PartitionName}: {pct,5:F1}% ({p.BytesDone / (1024.0 * 1024.0),6:F2} / {p.BytesTotal / (1024.0 * 1024.0),6:F2} MiB) [{speedStr,-10}]     ");
            }

            var rc = await DumpRawprogramRunner.RunAsync(manager, dumpSaveDir, lun, genXmlOnly, skipSet, Progress);
            if (lastName != null)
            {
                Console.WriteLine();
            }
            return rc;
        });
    }
}