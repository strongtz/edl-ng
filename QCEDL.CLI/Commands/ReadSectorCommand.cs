using System.CommandLine;
using System.Diagnostics;
using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;

namespace QCEDL.CLI.Commands;

internal sealed class ReadSectorCommand
{
    private static readonly Argument<ulong> StartSectorArgument = new("start_sector", "The starting sector LBA to read from.");
    private static readonly Argument<ulong> SectorsArgument = new("sectors", "The number of sectors to read.");
    private static readonly Argument<FileInfo> FilenameArgument = new("filename", "The file to save the read data to.") { Arity = ArgumentArity.ExactlyOne };

    private static readonly Option<uint> LunOption = new(
        aliases: ["--lun", "-u"],
        description: "Specify the LUN number to read from.",
        getDefaultValue: () => 0);

    public static Command Create(GlobalOptionsBinder globalOptionsBinder)
    {
        var command = new Command("read-sector", "Reads a specified number of sectors from a given LUN and start LBA, saving to a file.")
        {
            StartSectorArgument,
            SectorsArgument,
            FilenameArgument,
            LunOption // Command-specific LUN
        };

        command.SetHandler(ExecuteAsync,
            globalOptionsBinder,
            StartSectorArgument,
            SectorsArgument,
            FilenameArgument,
            LunOption);

        return command;
    }

    public static Command CreateReadLunCommand(GlobalOptionsBinder globalOptionsBinder)
    {
        var command = new Command("read-lun", "Reads the entire LUN (all sectors) from a given LUN, saving to a file.")
        {
            FilenameArgument,
            LunOption // Command-specific LUN
        };

        command.SetHandler(ExecuteReadLunAsync,
            globalOptionsBinder,
            FilenameArgument,
            LunOption);

        return command;
    }

    private static async Task<int> ExecuteAsync(
        GlobalOptionsBinder globalOptions,
        ulong startSector,
        ulong sectorsToRead,
        FileInfo outputFile,
        uint lun)
    {
        Logging.Log($"Executing 'read-sector' command: LUN {lun}, Start LBA {startSector}, Sectors {sectorsToRead}, File '{outputFile.FullName}'...", LogLevel.Trace);

        return await ExecuteReadSectorsAsync(globalOptions, startSector, sectorsToRead, outputFile, lun, "read-sector");
    }

    private static async Task<int> ExecuteReadLunAsync(
        GlobalOptionsBinder globalOptions,
        FileInfo outputFile,
        uint lun)
    {
        Logging.Log($"Executing 'read-lun' command: LUN {lun}, File '{outputFile.FullName}'...", LogLevel.Trace);

        // For read-lun, we'll determine the total sectors dynamically
        return await ExecuteReadSectorsAsync(globalOptions, 0, 0, outputFile, lun, "read-lun", readEntireLun: true);
    }

    private static async Task<int> ExecuteReadSectorsAsync(
        GlobalOptionsBinder globalOptions,
        ulong startSector,
        ulong sectorsToRead,
        FileInfo outputFile,
        uint lun,
        string commandName,
        bool readEntireLun = false)
    {
        static void TryDeletePartialFile(FileInfo file, long expectedBytes)
        {
            try
            {
                if (file.Exists && expectedBytes > 0 && file.Length < expectedBytes)
                {
                    file.Delete();
                }
            }
            catch (Exception ex)
            {
                Logging.Log($"Could not delete partial file '{file.FullName}': {ex.Message}", LogLevel.Warning);
            }
        }

        return await CommandExecutor.RunAsync(commandName, async () =>
        {
            using var manager = new EdlManager(globalOptions);

            var effectiveLun = lun;
            if (manager.IsDirectMode && lun != 0)
            {
                Logging.Log("Warning: LUN parameter is ignored in direct mode.", LogLevel.Warning);
                effectiveLun = 0;
            }

            var geometry = await manager.GetStorageGeometryAsync(effectiveLun);
            var sectorSize = geometry.SectorSize;
            var targetDescription = manager.GetTargetDescription(effectiveLun);
            Logging.Log($"Using sector size: {sectorSize} bytes for {targetDescription}.", LogLevel.Debug);

            if (readEntireLun)
            {
                if (geometry.TotalSectors.HasValue && geometry.TotalSectors.Value > 0)
                {
                    sectorsToRead = geometry.TotalSectors.Value;
                    Logging.Log($"Read-lun: Total blocks from storage info: {sectorsToRead}", LogLevel.Info);
                }
                else
                {
                    Logging.Log("Error: Could not determine total blocks for read-lun command. Storage info unavailable.", LogLevel.Error);
                    return 1;
                }
            }

            if (sectorsToRead == 0)
            {
                Logging.Log("Error: Number of sectors to read must be greater than 0.", LogLevel.Error);
                return 1;
            }

            ulong endSector;
            try
            {
                endSector = checked(startSector + sectorsToRead - 1);
            }
            catch (OverflowException)
            {
                Logging.Log("Error: Sector range exceeds supported bounds.", LogLevel.Error);
                return 1;
            }

            if (!manager.IsDirectMode && (startSector > uint.MaxValue || endSector > uint.MaxValue))
            {
                Logging.Log("Error: Sector range exceeds uint.MaxValue, which is not supported by the current Firehose.Read implementation.", LogLevel.Error);
                return 1;
            }

            decimal totalBytesDecimal;
            try
            {
                totalBytesDecimal = checked((decimal)sectorsToRead * sectorSize);
            }
            catch (OverflowException)
            {
                totalBytesDecimal = decimal.MaxValue;
            }

            if (totalBytesDecimal <= 0)
            {
                Logging.Log($"Warning: Calculated total bytes to read is {totalBytesDecimal}. Nothing to read.", LogLevel.Warning);
                await File.WriteAllBytesAsync(outputFile.FullName, []);
                return 0;
            }

            var totalBytesToRead = totalBytesDecimal > long.MaxValue ? long.MaxValue : (long)totalBytesDecimal;
            Logging.Log($"Preparing to read {sectorsToRead} sectors (LBA {startSector} to {endSector}, {totalBytesDecimal} bytes) from {targetDescription} into '{outputFile.FullName}'...");

            var readStopwatch = new Stopwatch();
            var progress = new ProgressReporter(readStopwatch, "Reading");

            try
            {
                outputFile.Directory?.Create();
                using var fileStream = outputFile.Open(FileMode.Create, FileAccess.Write, FileShare.None);

                readStopwatch.Start();
                await manager.ReadSectorsToStreamAsync(
                    effectiveLun,
                    startSector,
                    sectorsToRead,
                    fileStream,
                    progress.Report);
                readStopwatch.Stop();
            }
            catch (IOException ioEx)
            {
                Logging.Log($"IO Error creating/writing to file '{outputFile.FullName}': {ioEx.Message}", LogLevel.Error);
                Console.WriteLine();
                return 1;
            }
            catch
            {
                Console.WriteLine();
                TryDeletePartialFile(outputFile, totalBytesToRead);
                throw;
            }

            Console.WriteLine();

            var bytesReadReported = progress.BytesReported == 0 ? totalBytesToRead : progress.BytesReported;
            Logging.Log($"Successfully read {bytesReadReported / (1024.0 * 1024.0):F2} MiB and wrote to '{outputFile.FullName}' in {readStopwatch.Elapsed.TotalSeconds:F2}s.");

            return 0;
        });
    }
}