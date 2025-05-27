using System.CommandLine;
using Microsoft.Extensions.Logging;
using QCEDL.CLI.Core;
using QCEDL.CLI.Logging;
using QCEDL.NET.PartitionTable;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.JSON.StorageInfo;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace QCEDL.CLI.Commands;

internal sealed class PrintGptCommand(
    ILogger<PrintGptCommand> logger,
    GlobalOptionsBinder globalOptionsBinder,
    IEdlManagerProvider edlManagerProvider) : ICommand
{
    private static readonly Option<uint> LunOption = new(
        aliases: ["--lun", "-u"],
        description: "Specify the LUN number to read the GPT from.",
        getDefaultValue: () => 0);

    public Command Create()
    {
        var command =
            new Command("printgpt", "Reads and prints the GPT (GUID Partition Table) from the device.") { LunOption };

        command.SetHandler(
            ExecuteAsync,
            globalOptionsBinder,
            LunOption);

        return command;
    }

    private async Task<int> ExecuteAsync(GlobalOptionsBinder globalOptions, uint lun)
    {
        logger.ExecutingPrintGpt();

        try
        {
            using var manager = edlManagerProvider.CreateEdlManager();

            await manager.EnsureFirehoseModeAsync();
            await manager.ConfigureFirehoseAsync();

            logger.AttemptReadGpt(lun);

            var storageType = globalOptions.MemoryType ?? StorageType.UFS;
            // Get Storage Info to determine sector size more reliably
            Root? storageInfo = null;
            try
            {
                // Wrap GetStorageInfo in Task.Run if it's potentially blocking or synchronous
                storageInfo =
                    await Task.Run(() => manager.Firehose.GetStorageInfo(storageType, lun, globalOptions.Slot));
            }
            catch (Exception storageEx)
            {
                logger.StorageInfoError(lun, storageType, storageEx);
            }

            var sectorSize = storageInfo?.StorageInfo?.BlockSize > 0 ? (uint)storageInfo.StorageInfo.BlockSize : 4096;
            // Override based on known types if GetStorageInfo failed or returned invalid size
            if (sectorSize is <= 0 or > 1024 * 1024) // Basic sanity check
            {
                if (storageType == StorageType.NVME || storageType == StorageType.SDCC)
                {
                    sectorSize = 512;
                    logger.DefaultSectorSizeWithType(storageType, sectorSize);
                }
                else
                {
                    sectorSize = 4096;
                    logger.DefaultSectorSize(sectorSize);
                }
            }

            logger.UsingSectorSize(sectorSize, lun);

            // Read the first few sectors where GPT resides (Primary: 0-?, Backup: depends on disk size)
            // Reading 34 sectors is usually safe for primary GPT header + entries (1 header + 128 entries * 128 bytes / sectorSize = ~33 sectors)
            // Let's read a bit more just in case, but keep it reasonable. 64 sectors * 4k = 256k
            uint sectorsToRead = 64;
            var gptData = await Task.Run(() => manager.Firehose.Read(
                storageType,
                lun,
                globalOptions.Slot,
                sectorSize,
                0, // Start sector
                sectorsToRead - 1 // Last sector (inclusive)
            ));

            if (gptData == null || gptData.Length < sectorSize * 2) // Need at least MBR + GPT Header
            {
                logger.FailedToReadSufficientGpt(lun);
                return 1;
            }

            using var stream = new MemoryStream(gptData);
            try
            {
                var gpt = GPT.ReadFromStream(stream, (int)sectorSize);

                if (gpt == null)
                {
                    logger.NoValidGptFound(lun);
                    // Don't necessarily exit with error, maybe just no GPT exists
                    return 0;
                }

                logger.GptHeaderSection(lun);
                logger.GptSignature(gpt.Header.Signature);
                logger.GptRevision(gpt.Header.Revision);
                logger.GptHeaderSize(gpt.Header.Size);
                logger.GptHeaderCrc32(gpt.Header.CRC32);
                logger.GptCurrentLba(gpt.Header.CurrentLBA);
                logger.GptBackupLba(gpt.Header.BackupLBA);
                logger.GptFirstUsableLba(gpt.Header.FirstUsableLBA);
                logger.GptLastUsableLba(gpt.Header.LastUsableLBA);
                logger.GptDiskGuid(gpt.Header.DiskGUID);
                logger.GptPartitionArrayLba(gpt.Header.PartitionArrayLBA);
                logger.GptPartitionEntryCount(gpt.Header.PartitionEntryCount);
                logger.GptPartitionEntrySize(gpt.Header.PartitionEntrySize);
                logger.GptPartitionArrayCrc32(gpt.Header.PartitionArrayCRC32);
                logger.GptIsBackup(gpt.IsBackup);

                logger.GptPartitionsSection(lun);

                if (gpt.Partitions.Count == 0)
                {
                    logger.NoPartitionsFound();
                }
                else
                {
                    foreach (var partition in gpt.Partitions)
                    {
                        // Clean up partition name (remove null terminators)
                        var partitionName = partition.GetName();
                        var firstLba = partition.FirstLBA;
                        var lastLba = partition.LastLBA;
                        var sizeMiB = (lastLba - firstLba + 1) * sectorSize / 1024.0 / 1024.0;

                        logger.PartitionName(partitionName);
                        logger.PartitionTypeGuid(partition.TypeGUID);
                        logger.PartitionUid(partition.UID);
                        logger.PartitionLba(firstLba, lastLba, sizeMiB);
                        logger.PartitionAttributes(partition.Attributes);
                    }
                }
            }
            catch (InvalidDataException ex)
            {
                logger.ErrorParsingGptData(lun, ex.Message);
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
        catch (Exception ex)
        {
            logger.UnexceptedException(ex);
            return 1;
        }

        return 0;
    }
}