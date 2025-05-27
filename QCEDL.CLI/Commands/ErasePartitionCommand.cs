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

internal sealed class ErasePartitionCommand(
    ILogger<ErasePartitionCommand> logger,
    GlobalOptionsBinder globalOptionsBinder,
    IEdlManagerProvider edlManagerProvider) : ICommand
{
    private static readonly Argument<string> PartitionNameArgument =
        new("partition_name", "The name of the partition to erase.");

    private static readonly Option<uint?> LunOption = new(
        aliases: ["--lun", "-u"],
        description: "Specify the LUN number. If not specified, all LUNs will be scanned for the partition.");

    public Command Create()
    {
        var command = new Command("erase-part", "Erases a partition by name from the device.")
        {
            PartitionNameArgument, LunOption
        };

        command.SetHandler(
            ExecuteAsync,
            globalOptionsBinder,
            PartitionNameArgument,
            LunOption);

        return command;
    }

    private async Task<int> ExecuteAsync(
        GlobalOptionsBinder globalOptions,
        string partitionName,
        uint? specifiedLun)
    {
        logger.ExecutingErasePartition(partitionName);
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
                logger.ScanningSpecifiedLun(specifiedLun);
            }
            else
            {
                logger.ScanningAllLun();
                Root? devInfo = null;
                try
                {
                    devInfo = await Task.Run(() =>
                        manager.Firehose.GetStorageInfo(storageType, 0,
                            globalOptions.Slot)); // Check LUN 0 for num_physical
                }
                catch (Exception ex)
                {
                    logger.CannotGetLunCount(ex);
                }

                if (devInfo?.StorageInfo?.NumPhysical > 0)
                {
                    for (uint i = 0; i < devInfo.StorageInfo.NumPhysical; i++)
                    {
                        lunsToScan.Add(i);
                    }

                    logger.LunFound(devInfo.StorageInfo.NumPhysical, lunsToScan);
                }
                else
                {
                    if (storageType == StorageType.SPINOR)
                    {
                        lunsToScan.Add(0);
                    }
                    else
                    {
                        lunsToScan.AddRange([0, 1, 2, 3, 4, 5]);

                        logger.ScanningDefaultLuns(lunsToScan);
                    }
                }
            }

            foreach (var currentLun in lunsToScan)
            {
                logger.ScanningLun(currentLun, partitionName);
                Root? lunStorageInfo = null;
                try
                {
                    lunStorageInfo = await Task.Run(() =>
                        manager.Firehose.GetStorageInfo(storageType, currentLun, globalOptions.Slot));
                }
                catch (Exception storageEx)
                {
                    logger.SkippingLun(currentLun, storageEx);
                    continue;
                }

                var currentSectorSize = lunStorageInfo?.StorageInfo?.BlockSize > 0
                    ? (uint)lunStorageInfo.StorageInfo.BlockSize
                    : 0;
                if (currentSectorSize == 0)
                {
                    currentSectorSize =
                        storageType switch { StorageType.NVME => 512, StorageType.SDCC => 512, _ => 4096 };
                    logger.StorageInfoUnreliable(currentLun, storageType, currentSectorSize);
                }

                logger.SectorSize(currentLun, currentSectorSize);

                uint sectorsForGptRead = 64;
                byte[]? gptData;
                try
                {
                    gptData = await Task.Run(() => manager.Firehose.Read(storageType, currentLun, globalOptions.Slot,
                        currentSectorSize, 0, sectorsForGptRead - 1));
                }
                catch (Exception readEx)
                {
                    logger.FailedToReadGpt(currentLun, readEx);
                    continue;
                }

                if (gptData == null || gptData.Length < currentSectorSize * 2)
                {
                    logger.GptDataInsufficient(currentLun);
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
                            var currentPartitionName = p.GetName();
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
                catch (InvalidDataException ex)
                {
                    logger.GptNotFound(currentLun, ex);
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

            var partStartSectorUlong = foundPartition.Value.FirstLBA;
            var partLastSectorUlong = foundPartition.Value.LastLBA;
            var numSectorsToEraseUlong = partLastSectorUlong - partStartSectorUlong + 1;

            if (partStartSectorUlong > uint.MaxValue || numSectorsToEraseUlong > uint.MaxValue ||
                partStartSectorUlong + numSectorsToEraseUlong - 1 > uint.MaxValue)
            {
                logger.PartitionSectorRangeTooLarge(partitionName,
                    partStartSectorUlong,
                    numSectorsToEraseUlong);
                return 1;
            }

            var startSector = (uint)partStartSectorUlong;
            var numSectorsToErase = (uint)numSectorsToEraseUlong;

            if (numSectorsToErase == 0)
            {
                logger.PartitionZeroSize(partitionName);
                return 0;
            }

            logger.ErasingPartition(partitionName, actualLun, startSector, numSectorsToErase);
            var eraseStopwatch = Stopwatch.StartNew();

            var success = await Task.Run(() => manager.Firehose.Erase(
                storageType,
                actualLun,
                globalOptions.Slot,
                actualSectorSize,
                startSector,
                numSectorsToErase
            ));
            eraseStopwatch.Stop();

            if (success)
            {
                logger.PartitionErased(partitionName, eraseStopwatch.Elapsed.TotalSeconds);
            }
            else
            {
                logger.PartitionEraseFailed(partitionName);
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
        catch (InvalidOperationException ex)
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
            logger.ErasePartCommandFinished(commandStopwatch.Elapsed);
        }

        return 0;
    }
}