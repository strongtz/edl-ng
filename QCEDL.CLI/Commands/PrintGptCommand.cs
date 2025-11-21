using System.CommandLine;
using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;
using QCEDL.NET.PartitionTable;

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
            var isDirectMode = manager.IsHostDeviceMode || manager.IsRadxaWosMode;
            var effectiveLun = isDirectMode ? 0u : lun;

            var geometry = await manager.GetStorageGeometryAsync(effectiveLun);
            var sectorSize = geometry.SectorSize;
            var targetDescription = manager.IsHostDeviceMode
                ? "host device"
                : manager.IsRadxaWosMode
                    ? "Radxa WoS platform"
                    : $"LUN {effectiveLun}";

            Logging.Log($"Using sector size: {sectorSize} bytes for {targetDescription}.", LogLevel.Debug);

            const uint sectorsToRead = 64;
            var gptData = await manager.ReadSectorsAsync(effectiveLun, 0, sectorsToRead);

            if (gptData.Length < sectorSize * 2)
            {
                Logging.Log("Failed to read sufficient data for GPT.", LogLevel.Error);
                return 1;
            }

            var deviceDescription = targetDescription;
            return ProcessGptData(gptData, sectorSize, deviceDescription);
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
        catch (PlatformNotSupportedException ex)
        {
            Logging.Log($"Platform Error: {ex.Message}", LogLevel.Error);
            return 1;
        }
        catch (Exception ex)
        {
            Logging.Log($"An unexpected error occurred in 'printgpt': {ex.Message}", LogLevel.Error);
            Logging.Log(ex.ToString(), LogLevel.Debug);
            return 1;
        }
    }

    private static int ProcessGptData(byte[] gptData, uint sectorSize, string deviceDescription)
    {
        using var stream = new MemoryStream(gptData);
        try
        {
            var gpt = Gpt.ReadFromStream(stream, (int)sectorSize);

            if (gpt == null)
            {
                Logging.Log($"No valid GPT found on {deviceDescription}.", LogLevel.Warning);
                // Don't necessarily exit with error, maybe just no GPT exists
                return 0;
            }

            Logging.Log($"--- GPT Header {deviceDescription} ---");
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
            Logging.Log($"--- Partitions {deviceDescription} ---");

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
            Logging.Log($"Error parsing GPT data from {deviceDescription}: {ex.Message}", LogLevel.Error);
            return 1;
        }

        return 0;
    }
}