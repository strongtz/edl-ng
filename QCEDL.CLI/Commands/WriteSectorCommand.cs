using System.CommandLine;
using System.Diagnostics;
using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;

namespace QCEDL.CLI.Commands;

internal sealed class WriteSectorCommand
{
    private static readonly Argument<ulong> StartSectorArgument = new("start_sector", "The starting sector LBA to write to.");
    private static readonly Argument<FileInfo> FilenameArgument =
        new("filename", "The file containing data to write.")
        {
            Arity = ArgumentArity.ExactlyOne
        };

    private static readonly Option<uint> LunOption = new(
        aliases: ["--lun", "-u"],
        description: "Specify the LUN number to write to.",
        getDefaultValue: () => 0);

    public static Command Create(GlobalOptionsBinder globalOptionsBinder)
    {
        var command = new Command("write-sector", "Writes data from a file to a specified number of sectors from a given LUN and start LBA.")
        {
            StartSectorArgument,
            FilenameArgument,
            LunOption
        };

        _ = FilenameArgument.ExistingOnly();

        command.SetHandler(ExecuteAsync,
            globalOptionsBinder,
            StartSectorArgument,
            FilenameArgument,
            LunOption);

        return command;
    }

    private static async Task<int> ExecuteAsync(
        GlobalOptionsBinder globalOptions,
        ulong startSector,
        FileInfo inputFile,
        uint lun)
    {
        Logging.Log($"Executing 'write-sector' command: LUN {lun}, Start LBA {startSector}, File '{inputFile.FullName}'...", LogLevel.Trace);

        if (!inputFile.Exists)
        {
            Logging.Log($"Error: Input file '{inputFile.FullName}' not found.", LogLevel.Error);
            return 1;
        }

        if (inputFile.Length == 0)
        {
            Logging.Log("Error: Input file is empty. Nothing to write.", LogLevel.Error);
            return 1;
        }

        return await CommandExecutor.RunAsync("write-sector", async () =>
        {
            using var manager = new EdlManager(globalOptions);
            var effectiveLun = manager.IsDirectMode ? 0u : lun;
            if (manager.IsDirectMode && lun != 0)
            {
                Logging.Log("Warning: LUN parameter is ignored in direct mode.", LogLevel.Warning);
            }

            var geometry = await manager.GetStorageGeometryAsync(effectiveLun);
            var sectorSize = geometry.SectorSize;

            var paddedBytes = AlignmentHelper.AlignTo((ulong)inputFile.Length, sectorSize);
            if (paddedBytes == 0)
            {
                Logging.Log("No data to write after alignment.", LogLevel.Warning);
                return 0;
            }

            var sectorsToWrite = paddedBytes / sectorSize;
            if (!manager.IsDirectMode)
            {
                try
                {
                    checked
                    {
                        var endSector = startSector + sectorsToWrite - 1;
                        Logging.Log($"Calculated end sector: {endSector}", LogLevel.Debug);
                    }
                }
                catch (OverflowException)
                {
                    Logging.Log("Error: Sector range exceeds supported bounds.", LogLevel.Error);
                    return 1;
                }
            }

            var targetDescription = manager.GetTargetDescription(effectiveLun);
            Logging.Log($"Writing {sectorsToWrite} sectors ({paddedBytes} bytes) to {targetDescription}, starting at sector {startSector}...");

            var writeStopwatch = Stopwatch.StartNew();
            var progress = new ProgressReporter(writeStopwatch, "Writing");

            await using var fileStream = inputFile.OpenRead();
            await manager.WriteSectorsFromStreamAsync(
                effectiveLun,
                startSector,
                fileStream,
                fileStream.Length,
                padToSector: true,
                inputFile.Name,
                progress.Report);

            writeStopwatch.Stop();
            Console.WriteLine();

            var bytesWrittenReported = progress.BytesReported == 0 && paddedBytes > 0
                ? (long)Math.Min(paddedBytes, long.MaxValue)
                : progress.BytesReported;
            Logging.Log($"Successfully wrote {bytesWrittenReported / (1024.0 * 1024.0):F2} MiB in {writeStopwatch.Elapsed.TotalSeconds:F2}s.");

            return 0;
        });
    }

}