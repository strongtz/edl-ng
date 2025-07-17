using System.CommandLine;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.JSON.StorageInfo;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

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
        var commandStopwatch = Stopwatch.StartNew();

        try
        {
            using var manager = new EdlManager(globalOptions);
            await manager.EnsureFirehoseModeAsync();
            await manager.ConfigureFirehoseAsync();

            var storageType = globalOptions.MemoryType ?? StorageType.Ufs;
            Logging.Log($"Using storage type: {storageType}", LogLevel.Debug);

            // Get storage info to determine sector size and total blocks
            Root? storageInfo = null;
            try
            {
                storageInfo = await Task.Run(() => manager.Firehose.GetStorageInfo(storageType, lun, globalOptions.Slot));
            }
            catch (Exception storageEx)
            {
                Logging.Log($"Could not get storage info for LUN {lun} (StorageType: {storageType}). Using default sector size. Error: {storageEx.Message}", LogLevel.Warning);
            }

            var sectorSize = storageInfo?.StorageInfo?.BlockSize > 0 ? (uint)storageInfo.StorageInfo.BlockSize : 0;
            if (sectorSize == 0) // Fallback if GetStorageInfo failed or returned 0
            {
                sectorSize = storageType switch
                {
                    StorageType.Nvme => 512,
                    StorageType.Sdcc => 512,
                    StorageType.Spinor or StorageType.Ufs or StorageType.Nand or _ => 4096,
                };
                Logging.Log($"Storage info unreliable or unavailable, using default sector size for {storageType}: {sectorSize}", LogLevel.Warning);
            }
            Logging.Log($"Using sector size: {sectorSize} bytes for LUN {lun}.", LogLevel.Debug);

            // For read-lun, determine total sectors from storage info
            if (readEntireLun)
            {
                if (storageInfo?.StorageInfo?.TotalBlocks > 0)
                {
                    sectorsToRead = (ulong)storageInfo.StorageInfo.TotalBlocks;
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

            if (startSector > uint.MaxValue || startSector + sectorsToRead - 1 > uint.MaxValue)
            {
                Logging.Log("Error: Sector range exceeds uint.MaxValue, which is not supported by the current Firehose.Read implementation.", LogLevel.Error);
                return 1;
            }

            var firstLba = (uint)startSector;
            var lastLba = (uint)(startSector + sectorsToRead - 1);
            var totalBytesToRead = (long)sectorsToRead * sectorSize;

            if (totalBytesToRead <= 0)
            {
                Logging.Log($"Warning: Calculated total bytes to read is {totalBytesToRead}. Nothing to read.", LogLevel.Warning);
                await File.WriteAllBytesAsync(outputFile.FullName, []);
                return 0;
            }

            Logging.Log($"Preparing to read {sectorsToRead} sectors (LBA {firstLba} to {lastLba}, {totalBytesToRead} bytes) from LUN {lun} into '{outputFile.FullName}'...");

            long bytesReadReported = 0;
            var readStopwatch = new Stopwatch();

            void ProgressAction(long current, long total)
            {
                bytesReadReported = current;
                var percentage = total == 0 ? 100 : current * 100.0 / total;
                var elapsed = readStopwatch.Elapsed;
                var speed = current / elapsed.TotalSeconds;
                var speedStr = "N/A";
                if (elapsed.TotalSeconds > 0.1)
                {
                    speedStr = speed > 1024 * 1024 ? $"{speed / (1024 * 1024):F2} MiB/s" :
                        speed > 1024 ? $"{speed / 1024:F2} KiB/s" :
                        $"{speed:F0} B/s";
                }
                Console.Write($"\rReading: {percentage:F1}% ({current / (1024.0 * 1024.0):F2} / {total / (1024.0 * 1024.0):F2} MiB) [{speedStr}]      ");
            }

            bool success;
            try
            {
                outputFile.Directory?.Create();
                using var fileStream = outputFile.Open(FileMode.Create, FileAccess.Write, FileShare.None);

                readStopwatch.Start();
                success = await Task.Run(() => manager.Firehose.ReadToStream(
                    storageType,
                    lun,
                    globalOptions.Slot,
                    sectorSize,
                    firstLba,
                    lastLba,
                    fileStream,
                    ProgressAction
                ));
                readStopwatch.Stop();
            }
            catch (IOException ioEx)
            {
                Logging.Log($"IO Error creating/writing to file '{outputFile.FullName}': {ioEx.Message}", LogLevel.Error);
                Console.WriteLine();
                return 1;
            }

            Console.WriteLine(); // Newline after progress bar

            if (!success)
            {
                Logging.Log("Failed to read sector data or write to stream.", LogLevel.Error);
                try
                {
                    if (outputFile.Exists && outputFile.Length < totalBytesToRead)
                    {
                        outputFile.Delete();
                    }
                }
                catch (Exception ex) { Logging.Log($"Could not delete partial file '{outputFile.FullName}': {ex.Message}", LogLevel.Warning); }
                return 1;
            }

            Logging.Log($"Successfully read {bytesReadReported / (1024.0 * 1024.0):F2} MiB and wrote to '{outputFile.FullName}' in {readStopwatch.Elapsed.TotalSeconds:F2}s.");

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
            Logging.Log($"IO Error (e.g., writing file): {ex.Message}", LogLevel.Error);
            return 1;
        }
        catch (Exception ex)
        {
            Logging.Log($"An unexpected error occurred in '{commandName}': {ex.Message}", LogLevel.Error);
            Logging.Log(ex.ToString(), LogLevel.Debug);
            return 1;
        }
        finally
        {
            commandStopwatch.Stop();
            Logging.Log($"'{commandName}' command finished in {commandStopwatch.Elapsed.TotalSeconds:F2}s.", LogLevel.Debug);
        }

        return 0;
    }
}