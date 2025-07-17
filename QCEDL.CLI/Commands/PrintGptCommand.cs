using System.CommandLine;
using Microsoft.Extensions.Logging;
using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;
using QCEDL.NET.PartitionTable;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.JSON.StorageInfo;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace QCEDL.CLI.Commands;

internal sealed class PrintGptCommand
{
    private static readonly Option<uint> LunOption = new(
        aliases: ["--lun", "-u"],
        description: "Specify the LUN number to read the GPT from.",
        getDefaultValue: () => 0);

    public static Command Create(GlobalOptionsBinder globalOptionsBinder)
    {
        var command = new Command("printgpt", "Reads and prints the GPT (GUID Partition Table) from the device.")
        {
            LunOption
        };
        command.SetHandler(ExecuteAsync, globalOptionsBinder, LunOption);
        return command;
    }

    private static async Task<int> ExecuteAsync(GlobalOptionsBinder globalOptions, uint lun)
    {
        Logging.Log("Executing 'printgpt' command...", LogLevel.Trace);

        try
        {
            using var manager = new EdlManager(globalOptions);

            await manager.EnsureFirehoseModeAsync();
            await manager.ConfigureFirehoseAsync();

            Logging.Log($"Attempting to read GPT from LUN {lun}...");

            var storageType = globalOptions.MemoryType ?? StorageType.Ufs;
            // Get Storage Info to determine sector size more reliably
            Root? storageInfo = null;
            try
            {
                // Wrap GetStorageInfo in Task.Run if it's potentially blocking or synchronous
                storageInfo = await Task.Run(() => manager.Firehose.GetStorageInfo(storageType, lun, globalOptions.Slot));
            }
            catch (Exception storageEx)
            {
                Logging.Log($"Could not get storage info for LUN {lun} (StorageType: {storageType}). Using default sector size. Error: {storageEx.Message}", LogLevel.Error);
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
                Logging.Log($"Failed to read sufficient data for GPT from LUN {lun}.", LogLevel.Error);
                return 1;
            }

            using var stream = new MemoryStream(gptData);
            try
            {
                var gpt = Gpt.ReadFromStream(stream, (int)sectorSize);

                if (gpt == null)
                {
                    Logging.Log($"No valid GPT found on LUN {lun}.", LogLevel.Warning);
                    // Don't necessarily exit with error, maybe just no GPT exists
                    return 0;
                }

                Logging.Log($"--- GPT Header LUN {lun} ---");
                Logging.Log($"Signature: {gpt.Header.Signature}");
                Logging.Log($"Revision: {gpt.Header.Revision:X8}");
                Logging.Log($"Header Size: {gpt.Header.Size}");
                Logging.Log($"Header CRC32: {gpt.Header.CRC32:X8}");
                Logging.Log($"Current LBA: {gpt.Header.CurrentLBA}");
                Logging.Log($"Backup LBA: {gpt.Header.BackupLBA}");
                Logging.Log($"First Usable LBA: {gpt.Header.FirstUsableLBA}");
                Logging.Log($"Last Usable LBA: {gpt.Header.LastUsableLBA}");
                Logging.Log($"Disk GUID: {gpt.Header.DiskGUID}");
                Logging.Log($"Partition Array LBA: {gpt.Header.PartitionArrayLBA}");
                Logging.Log($"Partition Entry Count: {gpt.Header.PartitionEntryCount}");
                Logging.Log($"Partition Entry Size: {gpt.Header.PartitionEntrySize}");
                Logging.Log($"Partition Array CRC32: {gpt.Header.PartitionArrayCRC32:X8}");
                Logging.Log($"Is Backup GPT: {gpt.IsBackup}", LogLevel.Debug);
                Logging.Log($"--- Partitions LUN {lun} ---");

                if (gpt.Partitions.Count == 0)
                {
                    Logging.Log("No partitions found in GPT.", LogLevel.Warning);
                }
                else
                {
                    foreach (var partition in gpt.Partitions)
                    {
                        // Clean up partition name (remove null terminators)
                        var partitionName = partition.GetName().TrimEnd('\0');
                        Logging.Log($"  Name: {partitionName}");
                        Logging.Log($"    Type: {partition.TypeGUID}");
                        Logging.Log($"    UID:  {partition.UID}");
                        Logging.Log($"    LBA:  {partition.FirstLBA}-{partition.LastLBA} (Size: {(partition.LastLBA - partition.FirstLBA + 1) * sectorSize / 1024.0 / 1024.0:F2} MiB)");
                        Logging.Log($"    Attr: {partition.Attributes:X16}", LogLevel.Debug);
                    }
                }
            }
            catch (InvalidDataException ex)
            {
                Logging.Log($"Error parsing GPT data from LUN {lun}: {ex.Message}", LogLevel.Error);
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
        catch (Exception ex)
        {
            Logging.Log($"An unexpected error occurred in 'printgpt': {ex.Message}", LogLevel.Error);
            Logging.Log(ex.ToString(), LogLevel.Debug);
            return 1;
        }

        return 0;
    }
}