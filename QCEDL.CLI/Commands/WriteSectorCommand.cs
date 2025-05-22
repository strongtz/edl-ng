using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;
using System.CommandLine;
using System.Diagnostics;

namespace QCEDL.CLI.Commands;

internal sealed class WriteSectorCommand
{
    private static readonly Argument<ulong> StartSectorArgument = new("start_sector", "The starting sector LBA to write to.");
    private static readonly Argument<FileInfo> FilenameArgument =
        new("filename", "The file containing data to write.")
            { Arity = ArgumentArity.ExactlyOne };

    private static readonly Option<uint> LunOption = new Option<uint>(
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

        FilenameArgument.ExistingOnly();

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
        var commandStopwatch = Stopwatch.StartNew();

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
            await manager.EnsureFirehoseModeAsync();
            await manager.ConfigureFirehoseAsync();

            var storageType = globalOptions.MemoryType ?? StorageType.UFS;
            Logging.Log($"Using storage type: {storageType}", LogLevel.Debug);

            Qualcomm.EmergencyDownload.Layers.APSS.Firehose.JSON.StorageInfo.Root? storageInfo = null;
            try
            {
                storageInfo = await Task.Run(() => manager.Firehose.GetStorageInfo(storageType, lun, globalOptions.Slot));
            }
            catch (Exception storageEx)
            {
                Logging.Log($"Could not get storage info for LUN {lun} (StorageType: {storageType}). Using default sector size. Error: {storageEx.Message}", LogLevel.Warning);
            }

            var sectorSize = storageInfo?.StorageInfo?.BlockSize > 0 ? (uint)storageInfo.StorageInfo.BlockSize : 0;
            if (sectorSize == 0)
            {
                sectorSize = storageType switch
                {
                    StorageType.NVME => 512,
                    StorageType.SDCC => 512,
                    _ => 4096,
                };
                Logging.Log($"Storage info unreliable or unavailable, using default sector size for {storageType}: {sectorSize}", LogLevel.Warning);
            }
            Logging.Log($"Using sector size: {sectorSize} bytes for LUN {lun}.", LogLevel.Debug);

            var originalFileLength = inputFile.Length;
            long totalBytesToWriteIncludingPadding;
            uint numSectorsForXml;

            var remainder = originalFileLength % sectorSize;
            if (remainder != 0)
            {
                totalBytesToWriteIncludingPadding = originalFileLength + (sectorSize - remainder);
                Logging.Log($"Input file size ({originalFileLength} bytes) is not a multiple of sector size ({sectorSize} bytes). Padding with zeros to {totalBytesToWriteIncludingPadding} bytes.", LogLevel.Warning);
            }
            else
            {
                totalBytesToWriteIncludingPadding = originalFileLength;
            }
            numSectorsForXml = (uint)(totalBytesToWriteIncludingPadding / sectorSize);

            Logging.Log($"Data to write: {originalFileLength} bytes from file, padded to {totalBytesToWriteIncludingPadding} bytes ({numSectorsForXml} sectors).", LogLevel.Debug);
 
            if (startSector > uint.MaxValue)
            {
                Logging.Log($"Error: Start sector LBA ({startSector}) exceeds uint.MaxValue, which is not supported by the current Firehose.ProgramFromStream implementation's start_sector parameter.", LogLevel.Error);
                return 1;
            }

            Logging.Log($"Attempting to write {numSectorsForXml} sectors ({totalBytesToWriteIncludingPadding} bytes) to LUN {lun}, starting at LBA {startSector}...", LogLevel.Info);

            long bytesWrittenReported = 0;
            var writeStopwatch = new Stopwatch();

            Action<long, long> progressAction = (current, total) =>
            {
                bytesWrittenReported = current;
                var percentage = total == 0 ? 100 : (double)current * 100.0 / total;
                var elapsed = writeStopwatch.Elapsed;
                var speed = current / elapsed.TotalSeconds;
                var speedStr = "N/A";
                if (elapsed.TotalSeconds > 0.1)
                {
                    speedStr = speed > (1024 * 1024) ? $"{speed / (1024 * 1024):F2} MiB/s" :
                        speed > 1024 ? $"{speed / 1024:F2} KiB/s" :
                        $"{speed:F0} B/s";
                }
                Console.Write($"\rWriting: {percentage:F1}% ({current / (1024.0 * 1024.0):F2} / {total / (1024.0 * 1024.0):F2} MiB) [{speedStr}]      ");
            };

            bool success;
            try
            {
                using var fileStream = inputFile.OpenRead();

                writeStopwatch.Start();
                success = await Task.Run(() => manager.Firehose.ProgramFromStream(
                    storageType,
                    lun,
                    globalOptions.Slot,
                    sectorSize,
                    (uint)startSector,
                    numSectorsForXml,
                    totalBytesToWriteIncludingPadding,
                    inputFile.Name,
                    fileStream,
                    progressAction
                ));
                writeStopwatch.Stop();
            }
            catch (IOException ioEx)
            {
                Logging.Log($"IO Error reading input file '{inputFile.FullName}': {ioEx.Message}", LogLevel.Error);
                Console.WriteLine();
                return 1;
            }

            Console.WriteLine(); // Newline after progress bar

            if (success)
            {
                Logging.Log($"Data ({bytesWrittenReported / (1024.0 * 1024.0):F2} MiB) written to sectors successfully in {writeStopwatch.Elapsed.TotalSeconds:F2}s.", LogLevel.Info);
            }
            else
            {
                Logging.Log("Failed to write data to sectors. Check previous logs for NAK or errors.", LogLevel.Error);
                return 1;
            }
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
        catch (Exception ex)
        {
            Logging.Log($"An unexpected error occurred in 'write-sector': {ex.Message}", LogLevel.Error);
            Logging.Log(ex.ToString(), LogLevel.Debug);
            return 1;
        }

        return 0;
    }
}