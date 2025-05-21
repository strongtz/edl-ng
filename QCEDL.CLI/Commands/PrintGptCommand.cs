using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;
using QCEDL.NET.PartitionTable;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;
using System.CommandLine;

namespace QCEDL.CLI.Commands
{
    internal class PrintGptCommand
    {
        private static readonly Option<uint> LunOption = new Option<uint>(
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

                Logging.Log($"Attempting to read GPT from LUN {lun}...", LogLevel.Info);

                StorageType storageType = globalOptions.MemoryType ?? StorageType.UFS;
                // Get Storage Info to determine sector size more reliably
                Qualcomm.EmergencyDownload.Layers.APSS.Firehose.JSON.StorageInfo.Root? storageInfo = null;
                try
                {
                    // Wrap GetStorageInfo in Task.Run if it's potentially blocking or synchronous
                    storageInfo = await Task.Run(() => manager.Firehose.GetStorageInfo(storageType, lun, globalOptions.Slot));
                }
                catch (Exception storageEx)
                {
                    Logging.Log($"Could not get storage info for LUN {lun} (StorageType: {storageType}). Using default sector size. Error: {storageEx.Message}", LogLevel.Error);
                }
                uint sectorSize = storageInfo?.storage_info?.block_size > 0 ? (uint)storageInfo.storage_info.block_size : 4096;
                // Override based on known types if GetStorageInfo failed or returned invalid size
                if (sectorSize <= 0 || sectorSize > (1024 * 1024)) // Basic sanity check
                {
                    if (storageType == StorageType.NVME || storageType == StorageType.SDCC)
                    {
                        sectorSize = 512;
                        Logging.Log($"Storage info unreliable, using default sector size for {storageType}: {sectorSize}", LogLevel.Warning);
                    }
                    else
                    {
                        sectorSize = 4096;
                        Logging.Log($"Storage info unreliable, using default sector size: {sectorSize}", LogLevel.Warning);
                    }
                }
                Logging.Log($"Using sector size: {sectorSize} bytes for LUN {lun}.", LogLevel.Debug);


                // Read the first few sectors where GPT resides (Primary: 0-?, Backup: depends on disk size)
                // Reading 34 sectors is usually safe for primary GPT header + entries (1 header + 128 entries * 128 bytes / sectorSize = ~33 sectors)
                // Let's read a bit more just in case, but keep it reasonable. 64 sectors * 4k = 256k
                uint sectorsToRead = 64;
                byte[] gptData = await Task.Run(() => manager.Firehose.Read(
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
                    var gpt = GPT.ReadFromStream(stream, (int)sectorSize);

                    if (gpt == null)
                    {
                        Logging.Log($"No valid GPT found on LUN {lun}.", LogLevel.Warning);
                        // Don't necessarily exit with error, maybe just no GPT exists
                        return 0;
                    }

                    Logging.Log($"--- GPT Header LUN {lun} ---", LogLevel.Info);
                    Logging.Log($"Signature: {new string(gpt.Header.Signature)}", LogLevel.Info);
                    Logging.Log($"Revision: {gpt.Header.Revision:X8}", LogLevel.Info);
                    Logging.Log($"Header Size: {gpt.Header.Size}", LogLevel.Info);
                    Logging.Log($"Header CRC32: {gpt.Header.CRC32:X8}", LogLevel.Info);
                    Logging.Log($"Current LBA: {gpt.Header.CurrentLBA}", LogLevel.Info);
                    Logging.Log($"Backup LBA: {gpt.Header.BackupLBA}", LogLevel.Info);
                    Logging.Log($"First Usable LBA: {gpt.Header.FirstUsableLBA}", LogLevel.Info);
                    Logging.Log($"Last Usable LBA: {gpt.Header.LastUsableLBA}", LogLevel.Info);
                    Logging.Log($"Disk GUID: {gpt.Header.DiskGUID}", LogLevel.Info);
                    Logging.Log($"Partition Array LBA: {gpt.Header.PartitionArrayLBA}", LogLevel.Info);
                    Logging.Log($"Partition Entry Count: {gpt.Header.PartitionEntryCount}", LogLevel.Info);
                    Logging.Log($"Partition Entry Size: {gpt.Header.PartitionEntrySize}", LogLevel.Info);
                    Logging.Log($"Partition Array CRC32: {gpt.Header.PartitionArrayCRC32:X8}", LogLevel.Info);
                    Logging.Log($"Is Backup GPT: {gpt.IsBackup}", LogLevel.Debug);
                    Logging.Log($"--- Partitions LUN {lun} ---", LogLevel.Info);

                    if (gpt.Partitions.Count == 0)
                    {
                        Logging.Log("No partitions found in GPT.", LogLevel.Warning);
                    }
                    else
                    {
                        foreach (var partition in gpt.Partitions)
                        {
                            // Clean up partition name (remove null terminators)
                            string partitionName = new string(partition.Name).TrimEnd('\0');
                            Logging.Log($"  Name: {partitionName}", LogLevel.Info);
                            Logging.Log($"    Type: {partition.TypeGUID}", LogLevel.Info);
                            Logging.Log($"    UID:  {partition.UID}", LogLevel.Info);
                            Logging.Log($"    LBA:  {partition.FirstLBA}-{partition.LastLBA} (Size: {(partition.LastLBA - partition.FirstLBA + 1) * sectorSize / 1024.0 / 1024.0:F2} MiB)", LogLevel.Info);
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
}
