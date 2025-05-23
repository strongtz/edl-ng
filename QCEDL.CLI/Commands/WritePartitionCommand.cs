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

internal sealed class WritePartitionCommand(
    ILogger<WritePartitionCommand> logger,
    GlobalOptionsBinder globalOptionsBinder,
    IEdlManagerProvider edlManagerProvider) : ICommand
{
    private static readonly Argument<string> PartitionNameArgument =
        new("partition_name", "The name of the partition to write.");

    private static readonly Argument<FileInfo> FilenameArgument =
        new("filename", "The file containing data to write to the partition.") { Arity = ArgumentArity.ExactlyOne };

    private static readonly Option<uint?> LunOption = new(
        aliases: ["--lun", "-u"],
        description: "Specify the LUN number. If not specified, all LUNs will be scanned for the partition.");

    static WritePartitionCommand()
    {
        FilenameArgument.ExistingOnly();
    }

    public Command Create()
    {
        var command = new Command("write-part", "Writes data from a file to a partition by name.")
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
        FileInfo inputFile,
        uint? specifiedLun)
    {
        logger.ExecutingWritePartition(partitionName, inputFile.FullName);
        var commandStopwatch = Stopwatch.StartNew();

        if (!inputFile.Exists)
        {
            logger.InputFileNotFound(inputFile.FullName);
            return 1;
        }

        if (inputFile.Length == 0)
        {
            logger.InputFileEmpty();
            return 1;
        }

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
                logger.NoLunSpecified();
                Root? devInfo = null;
                try
                {
                    devInfo = await Task.Run(() =>
                        manager.Firehose.GetStorageInfo(storageType, 0,
                            globalOptions.Slot)); // Check LUN 0 for num_physical
                }
                catch (Exception ex)
                {
                    logger.CouldNotGetDeviceInfo(ex);
                }

                if (devInfo?.StorageInfo?.NumPhysical > 0)
                {
                    for (uint i = 0; i < devInfo.StorageInfo.NumPhysical; i++)
                    {
                        lunsToScan.Add(i);
                    }

                    logger.DeviceReportsNumPhysicalLuns(devInfo.StorageInfo.NumPhysical, lunsToScan);
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
                        logger.CouldNotDetermineLunCount(lunsToScan);
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
                    logger.CouldNotGetStorageInfo(currentLun, storageEx);
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
                        StorageType.SDCC => 512, _ => 4096
                    };
                    logger.StorageInfoUnreliable(currentLun, storageType, currentSectorSize);
                }

                logger.UsingSectorSize(currentSectorSize, currentLun);

                uint sectorsForGptRead = 64;
                byte[]? gptData;
                try
                {
                    gptData = await Task.Run(() => manager.Firehose.Read(storageType, currentLun, globalOptions.Slot,
                        currentSectorSize, 0, sectorsForGptRead - 1));
                }
                catch (Exception readEx)
                {
                    logger.FailedToReadGptArea(currentLun, readEx);
                    continue;
                }

                if (gptData == null || gptData.Length < currentSectorSize * 2)
                {
                    logger.FailedToReadGptData(currentLun);
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
                                logger.FoundPartition(partitionName, actualLun, actualSectorSize);
                                logger.PartitionDetails(p.TypeGUID, p.UID, p.FirstLBA, p.LastLBA);
                                break;
                            }
                        }
                    }
                }
                catch (InvalidDataException ex)
                {
                    logger.NoValidGptOrParseError(currentLun, ex);
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
                logger.PartitionNotFoundOnLun(partitionName, specifiedLun);
                return 1;
            }

            var originalFileLength = inputFile.Length;
            long totalBytesToWriteIncludingPadding;

            var partitionSizeInBytes = (long)(foundPartition.Value.LastLBA - foundPartition.Value.FirstLBA + 1) *
                                       actualSectorSize;

            if (originalFileLength > partitionSizeInBytes)
            {
                logger.InputFileLargerThanPartition(originalFileLength, partitionName, partitionSizeInBytes);
                return 1;
            }

            var remainder = originalFileLength % actualSectorSize;
            if (remainder != 0)
            {
                totalBytesToWriteIncludingPadding = originalFileLength + (actualSectorSize - remainder);
                logger.InputFilePaddingWarning(originalFileLength, actualSectorSize, totalBytesToWriteIncludingPadding);
            }
            else
            {
                totalBytesToWriteIncludingPadding = originalFileLength;
            }

            if (totalBytesToWriteIncludingPadding > partitionSizeInBytes)
            {
                logger.PaddedDataLargerThanPartition(totalBytesToWriteIncludingPadding, partitionName,
                    partitionSizeInBytes);
                return 1;
            }

            var numSectorsForXml = (uint)(totalBytesToWriteIncludingPadding / actualSectorSize);

            logger.DataToWriteWithPadding(originalFileLength, totalBytesToWriteIncludingPadding, numSectorsForXml);
            if (totalBytesToWriteIncludingPadding < partitionSizeInBytes)
            {
                logger.PaddedDataSmallerThanPartition(partitionName, totalBytesToWriteIncludingPadding);
            }

            var partStartSector = foundPartition.Value.FirstLBA;
            if (partStartSector > uint.MaxValue)
            {
                logger.PartitionStartLbaExceedsMaxValue(partStartSector);
                return 1;
            }

            logger.AttemptingPartitionWrite(totalBytesToWriteIncludingPadding, partitionName, actualLun,
                partStartSector);

            long bytesWrittenReported = 0;
            var writeStopwatch = new Stopwatch();

            Action<long, long> progressAction = (current, total) =>
            {
                bytesWrittenReported = current;
                var percentage = total == 0 ? 100 : current * 100.0 / total;
                var elapsed = writeStopwatch.Elapsed;
                var speed = current / elapsed.TotalSeconds;
                var speedStr = "N/A";
                if (elapsed.TotalSeconds > 0.1)
                {
                    speedStr = speed > 1024 * 1024 ? $"{speed / (1024 * 1024):F2} MiB/s" :
                        speed > 1024 ? $"{speed / 1024:F2} KiB/s" :
                        $"{speed:F0} B/s";
                }

                Console.Write(
                    $"\rWriting: {percentage:F1}% ({current / (1024.0 * 1024.0):F2} / {total / (1024.0 * 1024.0):F2} MiB) [{speedStr}]      ");
            };

            bool success;
            try
            {
                await using var fileStream = inputFile.OpenRead();

                writeStopwatch.Start();
                success = await Task.Run(() => manager.Firehose.ProgramFromStream(
                    storageType,
                    actualLun,
                    globalOptions.Slot,
                    actualSectorSize,
                    (uint)partStartSector,
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
                logger.IoErrorReadingInputFile(inputFile.FullName, ioEx.Message);
                return 1;
            }

            Console.WriteLine(); // Newline after progress bar

            if (success)
            {
                logger.WritePartitionSucceeded(
                    bytesWrittenReported / (1024.0 * 1024.0),
                    partitionName,
                    writeStopwatch.Elapsed);
            }
            else
            {
                logger.WritePartitionFailed(partitionName);
                return 1;
            }
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
            logger.WritePartFinished(commandStopwatch.Elapsed.TotalSeconds);
        }

        return 0;
    }
}