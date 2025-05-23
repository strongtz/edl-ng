using System.CommandLine;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using QCEDL.CLI.Core;
using QCEDL.CLI.Logging;
using QCEDL.NET.PartitionTable;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.JSON.StorageInfo;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace QCEDL.CLI.Commands;

internal sealed class ReadPartitionCommand(
    ILogger<ReadPartitionCommand> logger,
    GlobalOptionsBinder globalOptionsBinder,
    IEdlManagerProvider edlManagerProvider) : ICommand
{
    private static readonly Argument<string> PartitionNameArgument =
        new("partition_name", "The name of the partition to read.");

    private static readonly Argument<FileInfo> FilenameArgument =
        new("filename", "The file to save the partition data to.") { Arity = ArgumentArity.ExactlyOne };

    private static readonly Option<uint?> LunOption = new(
        aliases: ["--lun", "-u"],
        description: "Specify the LUN number. If not specified, all LUNs will be scanned for the partition.");

    public Command Create()
    {
        var command = new Command("read-part", "Reads a partition by name from the device, saving to a file.")
        {
            PartitionNameArgument, FilenameArgument, LunOption
        };

        command.SetHandler(
            ExecuteAsync,
            globalOptionsBinder,
            PartitionNameArgument,
            FilenameArgument,
            LunOption);

        return command;
    }

    private async Task<int> ExecuteAsync(
        GlobalOptionsBinder globalOptions,
        string partitionName,
        FileInfo outputFile,
        uint? specifiedLun)
    {
        logger.ExecutingReadPartCommand(partitionName, outputFile.FullName);
        var commandStopwatch = Stopwatch.StartNew();

        try
        {
            using var manager = edlManagerProvider.CreateEdlManager();
            await manager.EnsureFirehoseModeAsync();
            await manager.ConfigureFirehoseAsync();

            var storageType = globalOptions.MemoryType ?? StorageType.UFS;
            logger.UsingStorageType(storageType);

            GPTPartition? foundPartition = null;
            uint actualLun = 0;
            uint actualSectorSize = 0;

            List<uint> lunsToScan = [];

            if (specifiedLun is not null)
            {
                lunsToScan.Add(specifiedLun.Value);
                logger.ScanningSpecifiedLun(specifiedLun.Value);
            }
            else
            {
                logger.NoLunSpecifiedScanAll();
                Root? devInfo = null;
                try
                {
                    // Attempt to get info from LUN 0, as it often contains num_physical
                    devInfo = await Task.Run(() => manager.Firehose.GetStorageInfo(storageType, 0, globalOptions.Slot));
                }
                catch (Exception ex)
                {
                    logger.CouldNotGetDeviceInfoForLun0(ex);
                }

                if (devInfo?.StorageInfo?.NumPhysical > 0)
                {
                    for (uint i = 0; i < devInfo.StorageInfo.NumPhysical; i++)
                    {
                        lunsToScan.Add(i);
                    }

                    logger.DeviceReportsAndScanningLuns(devInfo.StorageInfo.NumPhysical, lunsToScan);
                }
                else
                {
                    if (storageType == StorageType.SPINOR)
                    {
                        lunsToScan.Add(0);
                    }
                    else
                    {
                        // Fallback: scan a common range of LUNs if num_physical couldn't be determined
                        lunsToScan.AddRange([0, 1, 2, 3, 4, 5]);
                        logger.CouldNotDetermineLunCountDefault(lunsToScan);
                    }
                }
            }

            foreach (var currentLun in lunsToScan)
            {
                logger.ScanningLunForPartition(currentLun, partitionName);
                Root? lunStorageInfo;
                try
                {
                    lunStorageInfo = await Task.Run(() =>
                        manager.Firehose.GetStorageInfo(storageType, currentLun, globalOptions.Slot));
                }
                catch (Exception storageEx)
                {
                    logger.CouldNotGetStorageInfoForLun(currentLun, storageEx);
                    continue;
                }

                var currentSectorSize = lunStorageInfo?.StorageInfo?.BlockSize > 0
                    ? (uint)lunStorageInfo.StorageInfo.BlockSize
                    : 0;
                if (currentSectorSize == 0)
                {
                    currentSectorSize = storageType switch
                    {
                        StorageType.NVME => 512,
                        StorageType.SDCC => 512,
                        _ => 4096,
                    };
                    logger.StorageInfoUnreliableUseDefaultSectorSize(currentLun, storageType, currentSectorSize);
                }

                logger.UsingSectorSizeForLun(currentLun, currentSectorSize);

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
                    logger.FailedToReadGptAreaForLun(currentLun, readEx);
                    continue;
                }


                if (gptData == null || gptData.Length < currentSectorSize * 2)
                {
                    logger.FailedToReadSufficientGptData(currentLun);
                    continue;
                }

                using var stream = new MemoryStream(gptData);
                try
                {
                    var gpt = GPT.ReadFromStream(stream, (int)currentSectorSize);
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
                                logger.FoundPartitionOnLun(partitionName, actualLun, actualSectorSize);
                                logger.PartitionDetails(p.TypeGUID, p.UID, p.FirstLBA, p.LastLBA);
                                break;
                            }
                        }
                    }
                }
                catch (InvalidDataException)
                {
                    logger.NoValidGptFound(currentLun);
                }
                catch (Exception ex)
                {
                    logger.ErrorProcessingGpt(currentLun, ex);
                }

                if (foundPartition is not null)
                {
                    break;
                }
            }

            if (foundPartition is null)
            {
                logger.PartitionNotFound(partitionName, specifiedLun);
                return 1;
            }

            var partStartSector = foundPartition.Value.FirstLBA;
            var partLastSector = foundPartition.Value.LastLBA;
            var numSectorsToRead = partLastSector - partStartSector + 1;
            var totalBytesToRead = (long)numSectorsToRead * actualSectorSize;

            if (partStartSector > uint.MaxValue || partLastSector > uint.MaxValue)
            {
                logger.PartitionRangeExceedsUint(partStartSector, partLastSector);
                return 1;
            }

            if (totalBytesToRead <= 0)
            {
                logger.PartitionSizeZeroOrNegative(partitionName, totalBytesToRead);
                // Create an empty file
                await File.WriteAllBytesAsync(outputFile.FullName, []);
                return 0;
            }

            logger.PreparingToReadPartition(
                partitionName,
                actualLun,
                partStartSector,
                partLastSector,
                numSectorsToRead,
                totalBytesToRead,
                outputFile.FullName);

            long bytesReadReported = 0;
            var readStopwatch = new Stopwatch(); // For timing the read operation itself
            Action<long, long> progressAction = (current, total) =>
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

                Console.Write(
                    $"\rReading: {percentage:F1}% ({current / (1024.0 * 1024.0):F2} / {total / (1024.0 * 1024.0):F2} MiB) [{speedStr}]      ");
            };

            bool success;
            try
            {
                // Ensure directory exists
                outputFile.Directory?.Create();
                await using var fileStream = outputFile.Open(FileMode.Create, FileAccess.Write, FileShare.None);

                readStopwatch.Start();
                success = await Task.Run(() => manager.Firehose.ReadToStream(
                    storageType,
                    actualLun,
                    globalOptions.Slot,
                    actualSectorSize,
                    (uint)partStartSector,
                    (uint)partLastSector,
                    fileStream,
                    progressAction
                ));
                readStopwatch.Stop();
            }
            catch (IOException ioEx) // Catch specific IO exceptions from FileStream
            {
                logger.IoErrorWritingFile(outputFile.FullName, ioEx.Message);
                return 1;
            }

            Console.WriteLine();
            if (!success)
            {
                logger.FailedReadPartitionOrWrite(partitionName);
                // Attempt to clean up partially written file
                try
                {
                    if (outputFile.Exists && outputFile.Length < totalBytesToRead)
                    {
                        outputFile.Delete();
                    }
                }
                catch (Exception ex)
                {
                    logger.CouldNotDeletePartialFile(outputFile.FullName, ex);
                }

                return 1;
            }

            logger.SuccessfullyReadAndWrote(
                bytesReadReported / (1024.0 * 1024.0),
                outputFile.FullName,
                readStopwatch.Elapsed.TotalSeconds);
        }
        catch (FileNotFoundException ex)
        {
            logger.ExceptedException(ex);
            return 1;
        }
        catch (ArgumentException ex)
        {
            logger.ExceptedException(ex);
            return 1;
        }
        catch (IOException ex)
        {
            logger.ExceptedException(ex);
            return 1;
        }
        catch (Exception ex)
        {
            logger.UnexceptedException(ex);
            return 1;
        }
        finally
        {
            commandStopwatch.Stop();
            logger.ReadPartCommandFinished(commandStopwatch.Elapsed);
        }

        return 0;
    }
}