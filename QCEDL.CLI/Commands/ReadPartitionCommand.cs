using System.CommandLine;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;
using QCEDL.NET.PartitionTable;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.JSON.StorageInfo;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace QCEDL.CLI.Commands;

internal sealed class ReadPartitionCommand
{
    private static readonly Argument<string> PartitionNameArgument = new("partition_name", "The name of the partition to read.");
    private static readonly Argument<FileInfo> FilenameArgument = new("filename", "The file to save the partition data to.") { Arity = ArgumentArity.ExactlyOne };

    private static readonly Option<uint?> LunOption = new(
        aliases: ["--lun", "-u"],
        description: "Specify the LUN number. If not specified, all LUNs will be scanned for the partition.");

    public static Command Create(GlobalOptionsBinder globalOptionsBinder)
    {
        var command = new Command("read-part", "Reads a partition by name from the device, saving to a file.")
        {
            PartitionNameArgument,
            FilenameArgument,
            LunOption
        };

        command.SetHandler(ExecuteAsync,
            globalOptionsBinder,
            PartitionNameArgument,
            FilenameArgument,
            LunOption);

        return command;
    }

    private static async Task<int> ExecuteAsync(
        GlobalOptionsBinder globalOptions,
        string partitionName,
        FileInfo outputFile,
        uint? specifiedLun)
    {
        Logging.Log($"Executing 'read-part' command: Partition '{partitionName}', File '{outputFile.FullName}'...", LogLevel.Trace);
        var commandStopwatch = Stopwatch.StartNew();

        try
        {
            using var manager = new EdlManager(globalOptions);
            await manager.EnsureFirehoseModeAsync();
            await manager.ConfigureFirehoseAsync();

            var storageType = globalOptions.MemoryType ?? StorageType.Ufs;
            Logging.Log($"Using storage type: {storageType}", LogLevel.Debug);

            GptPartition? foundPartition = null;
            uint actualLun = 0;
            uint actualSectorSize = 0;

            List<uint> lunsToScan = [];

            if (specifiedLun.HasValue)
            {
                lunsToScan.Add(specifiedLun.Value);
                Logging.Log($"Scanning specified LUN: {specifiedLun.Value}", LogLevel.Debug);
            }
            else
            {
                Logging.Log("No LUN specified, attempting to determine number of LUNs and scan all.", LogLevel.Debug);
                Root? devInfo = null;
                try
                {
                    // Attempt to get info from LUN 0, as it often contains num_physical
                    devInfo = await Task.Run(() => manager.Firehose.GetStorageInfo(storageType, 0, globalOptions.Slot));
                }
                catch (Exception ex)
                {
                    Logging.Log($"Could not get device info to determine LUN count from LUN 0. Error: {ex.Message}. Will try a default range.", LogLevel.Warning);
                }

                if (devInfo?.StorageInfo?.NumPhysical > 0)
                {
                    for (uint i = 0; i < devInfo.StorageInfo.NumPhysical; i++)
                    {
                        lunsToScan.Add(i);
                    }
                    Logging.Log($"Device reports {devInfo.StorageInfo.NumPhysical} LUN(s). Scanning LUNs: {string.Join(", ", lunsToScan)}", LogLevel.Debug);
                }
                else
                {
                    if (storageType == StorageType.Spinor)
                    {
                        lunsToScan.Add(0);
                    }
                    else
                    {
                        // Fallback: scan a common range of LUNs if num_physical couldn't be determined
                        lunsToScan.AddRange([0, 1, 2, 3, 4, 5]);
                        Logging.Log($"Could not determine LUN count. Scanning default LUNs: {string.Join(", ", lunsToScan)}", LogLevel.Warning);
                    }
                }
            }

            foreach (var currentLun in lunsToScan)
            {
                Logging.Log($"Scanning LUN {currentLun} for partition '{partitionName}'...", LogLevel.Debug);
                Root? lunStorageInfo = null;
                try
                {
                    lunStorageInfo = await Task.Run(() => manager.Firehose.GetStorageInfo(storageType, currentLun, globalOptions.Slot));
                }
                catch (Exception storageEx)
                {
                    Logging.Log($"Could not get storage info for LUN {currentLun}. Error: {storageEx.Message}. Skipping LUN.", LogLevel.Warning);
                    continue;
                }

                var currentSectorSize = lunStorageInfo?.StorageInfo?.BlockSize > 0 ? (uint)lunStorageInfo.StorageInfo.BlockSize : 0;
                if (currentSectorSize == 0)
                {
                    currentSectorSize = storageType switch
                    {
                        StorageType.Nvme => 512,
                        StorageType.Sdcc => 512,
                        StorageType.Spinor or StorageType.Ufs or StorageType.Nand or _ => 4096,
                    };
                    Logging.Log($"Storage info for LUN {currentLun} unreliable, using default sector size for {storageType}: {currentSectorSize}", LogLevel.Warning);
                }
                Logging.Log($"Using sector size: {currentSectorSize} bytes for LUN {currentLun}.", LogLevel.Debug);

                // Read enough data for GPT (e.g., 64 sectors)
                uint sectorsForGptRead = 64; // As in PrintGptCommand
                byte[]? gptData;
                try
                {
                    gptData = await Task.Run(() => manager.Firehose.Read(
                        storageType,
                        currentLun,
                        globalOptions.Slot,
                        currentSectorSize,
                        0, // Start sector for GPT
                        sectorsForGptRead - 1 // Last sector for GPT
                    ));
                }
                catch (Exception readEx)
                {
                    Logging.Log($"Failed to read GPT area from LUN {currentLun}. Error: {readEx.Message}. Skipping LUN.", LogLevel.Warning);
                    continue;
                }


                if (gptData == null || gptData.Length < currentSectorSize * 2)
                {
                    Logging.Log($"Failed to read sufficient data for GPT from LUN {currentLun}.", LogLevel.Warning);
                    continue;
                }

                using var stream = new MemoryStream(gptData);
                try
                {
                    var gpt = Gpt.ReadFromStream(stream, (int)currentSectorSize);
                    if (gpt != null)
                    {
                        foreach (var p in gpt.Partitions)
                        {
                            var currentPartitionName = p.GetName().TrimEnd('\0');
                            if (currentPartitionName.Equals(partitionName, StringComparison.OrdinalIgnoreCase))
                            {
                                foundPartition = p;
                                actualLun = currentLun;
                                actualSectorSize = currentSectorSize;
                                Logging.Log($"Found partition '{partitionName}' on LUN {actualLun} with sector size {actualSectorSize}.");
                                Logging.Log($"  Details - Type: {p.TypeGUID}, UID: {p.UID}, LBA: {p.FirstLBA}-{p.LastLBA}", LogLevel.Debug);
                                break;
                            }
                        }
                    }
                }
                catch (InvalidDataException)
                {
                    Logging.Log($"No valid GPT found or parse error on LUN {currentLun}.", LogLevel.Debug);
                }
                catch (Exception ex)
                {
                    Logging.Log($"Error processing GPT on LUN {currentLun}: {ex.Message}", LogLevel.Warning);
                }

                if (foundPartition.HasValue)
                {
                    break;
                }
            }

            if (!foundPartition.HasValue)
            {
                Logging.Log($"Error: Partition '{partitionName}' not found on " + (specifiedLun.HasValue ? $"LUN {specifiedLun.Value}." : "any scanned LUN."), LogLevel.Error);
                return 1;
            }

            var partStartSector = foundPartition.Value.FirstLBA;
            var partLastSector = foundPartition.Value.LastLBA;
            var numSectorsToRead = partLastSector - partStartSector + 1;
            var totalBytesToRead = (long)numSectorsToRead * actualSectorSize;

            if (partStartSector > uint.MaxValue || partLastSector > uint.MaxValue)
            {
                Logging.Log($"Error: Partition sector range (LBA {partStartSector}-{partLastSector}) exceeds uint.MaxValue, which is not supported by the current Firehose.Read implementation.", LogLevel.Error);
                return 1;
            }

            if (totalBytesToRead <= 0)
            {
                Logging.Log($"Warning: Partition '{partitionName}' has zero or negative size ({totalBytesToRead} bytes). Nothing to read.", LogLevel.Warning);
                // Create an empty file
                await File.WriteAllBytesAsync(outputFile.FullName, []);
                return 0;
            }

            Logging.Log($"Preparing to read partition '{partitionName}' from LUN {actualLun}: LBA {partStartSector} to {partLastSector} ({numSectorsToRead} sectors, {totalBytesToRead} bytes) into '{outputFile.FullName}'...");

            long bytesReadReported = 0;
            var readStopwatch = new Stopwatch(); // For timing the read operation itself
            void ProgressAction(long current, long total)
            {
                bytesReadReported = current;
                var percentage = total == 0 ? 100 : current * 100.0 / total;
                var elapsed = readStopwatch.Elapsed;
                var speed = current / elapsed.TotalSeconds; // Bytes/sec
                var speedStr = "N/A";
                if (elapsed.TotalSeconds > 0.1) // Avoid division by zero or tiny numbers
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
                // Ensure directory exists
                outputFile.Directory?.Create();
                using var fileStream = outputFile.Open(FileMode.Create, FileAccess.Write, FileShare.None);

                readStopwatch.Start();
                success = await Task.Run(() => manager.Firehose.ReadToStream(
                    storageType,
                    actualLun,
                    globalOptions.Slot,
                    actualSectorSize,
                    (uint)partStartSector,
                    (uint)partLastSector,
                    fileStream,
ProgressAction
                ));
                readStopwatch.Stop();
            }
            catch (IOException ioEx) // Catch specific IO exceptions from FileStream
            {
                Logging.Log($"IO Error creating/writing to file '{outputFile.FullName}': {ioEx.Message}", LogLevel.Error);
                Console.WriteLine(); // Clear progress line
                return 1;
            }
            Console.WriteLine(); // Newline after progress bar
            if (!success)
            {
                Logging.Log($"Failed to read partition '{partitionName}' or write to stream.", LogLevel.Error);
                // Attempt to clean up partially written file
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
            Logging.Log($"Successfully read {bytesReadReported / (1024.0 * 1024.0):F2} MiB for partition '{partitionName}' and wrote to '{outputFile.FullName}' in {readStopwatch.Elapsed.TotalSeconds:F2}s.");

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
            Logging.Log($"An unexpected error occurred in 'read-part': {ex.Message}", LogLevel.Error);
            Logging.Log(ex.ToString(), LogLevel.Debug);
            return 1;
        }
        finally
        {
            commandStopwatch.Stop();
            Logging.Log($"'read-part' command finished in {commandStopwatch.Elapsed.TotalSeconds:F2}s.", LogLevel.Debug);
        }

        return 0;
    }
}