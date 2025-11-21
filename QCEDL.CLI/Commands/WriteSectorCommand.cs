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

        try
        {
            using var manager = new EdlManager(globalOptions);
            var isDirectMode = manager.IsHostDeviceMode || manager.IsRadxaWosMode;

            var effectiveLun = isDirectMode ? 0u : lun;
            if (isDirectMode && lun != 0)
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
            if (!isDirectMode)
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

            var targetDescription = manager.IsHostDeviceMode
                ? "host device"
                : manager.IsRadxaWosMode
                    ? "Radxa WoS platform"
                    : $"LUN {effectiveLun}";
            Logging.Log($"Writing {sectorsToWrite} sectors ({paddedBytes} bytes) to {targetDescription}, starting at sector {startSector}...");

            long bytesWrittenReported = 0;
            var writeStopwatch = Stopwatch.StartNew();

            void ProgressAction(long current, long total)
            {
                bytesWrittenReported = current;
                var percentage = total == 0 ? 100 : current * 100.0 / total;
                var elapsed = writeStopwatch.Elapsed;
                var speed = elapsed.TotalSeconds > 0 ? current / elapsed.TotalSeconds : 0;
                var speedStr = "N/A";
                if (elapsed.TotalSeconds > 0.1)
                {
                    speedStr = speed > 1024 * 1024 ? $"{speed / (1024 * 1024):F2} MiB/s" :
                        speed > 1024 ? $"{speed / 1024:F2} KiB/s" :
                        $"{speed:F0} B/s";
                }

                Console.Write($"\rWriting: {percentage:F1}% ({current / (1024.0 * 1024.0):F2} / {total / (1024.0 * 1024.0):F2} MiB) [{speedStr}]      ");
            }

            await using var fileStream = inputFile.OpenRead();
            await manager.WriteSectorsFromStreamAsync(
                effectiveLun,
                startSector,
                fileStream,
                fileStream.Length,
                padToSector: true,
                inputFile.Name,
                ProgressAction);

            writeStopwatch.Stop();
            Console.WriteLine();

            if (bytesWrittenReported == 0 && paddedBytes > 0)
            {
                bytesWrittenReported = (long)Math.Min(paddedBytes, long.MaxValue);
            }

            Logging.Log($"Successfully wrote {bytesWrittenReported / (1024.0 * 1024.0):F2} MiB in {writeStopwatch.Elapsed.TotalSeconds:F2}s.");
        }
        catch (FileNotFoundException ex)
        {
            Logging.Log(ex.Message, LogLevel.Error);
            return 1;
        }
        catch (ArgumentException ex)
        {
            Logging.Log(ex.Message, LogLevel.Error);
            return 1;
        }
        catch (IOException ex)
        {
            Logging.Log($"IO Error (e.g., reading input file): {ex.Message}", LogLevel.Error);
            return 1;
        }
        catch (PlatformNotSupportedException ex)
        {
            Logging.Log($"Platform Error: {ex.Message}", LogLevel.Error);
            return 1;
        }
        catch (Exception ex)
        {
            Logging.Log($"An unexpected error occurred in 'write-sector': {ex.Message}", LogLevel.Error);
            Logging.Log(ex.ToString(), LogLevel.Debug);
            return 1;
        }

        return 0;
    }

}