using System.CommandLine;
using System.Diagnostics;
using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;

namespace QCEDL.CLI.Commands;

internal sealed class ErasePartitionCommand
{
    private static readonly Argument<string> PartitionNameArgument = new("partition_name", "The name of the partition to erase.");
    private static readonly Option<uint?> LunOption = new(
        aliases: ["--lun", "-u"],
        description: "Specify the LUN number. If not specified, all LUNs will be scanned for the partition.");

    public static Command Create(GlobalOptionsBinder globalOptionsBinder)
    {
        var command = new Command("erase-part", "Erases a partition by name from the device.")
        {
            PartitionNameArgument,
            LunOption
        };

        command.SetHandler(ExecuteAsync,
            globalOptionsBinder,
            PartitionNameArgument,
            LunOption);

        return command;
    }

    private static async Task<int> ExecuteAsync(
        GlobalOptionsBinder globalOptions,
        string partitionName,
        uint? specifiedLun)
    {
        Logging.Log($"Executing 'erase-part' command: Partition '{partitionName}'...", LogLevel.Trace);

        return await CommandExecutor.RunAsync("erase-part", async () =>
        {
            using var manager = new EdlManager(globalOptions);

            var partitionInfo = await manager.FindPartitionWithLunAsync(partitionName, specifiedLun);
            if (!partitionInfo.HasValue)
            {
                Logging.Log($"Error: Partition '{partitionName}' not found on {(specifiedLun.HasValue ? $"LUN {specifiedLun.Value}" : "any scanned LUN")}.", LogLevel.Error);
                return 1;
            }

            var (partition, actualLun) = partitionInfo.Value;
            var sectorCount = partition.LastLBA - partition.FirstLBA + 1;
            if (sectorCount == 0)
            {
                Logging.Log($"Warning: Partition '{partitionName}' has zero size. Nothing to erase.", LogLevel.Warning);
                return 0;
            }

            var geometry = await manager.GetStorageGeometryAsync(actualLun);
            var targetDescription = manager.GetTargetDescription(actualLun);
            Logging.Log($"Erasing partition '{partitionName}' ({targetDescription}, LBA {partition.FirstLBA}-{partition.LastLBA}, {sectorCount} sectors)...", LogLevel.Info);

            var eraseStopwatch = Stopwatch.StartNew();
            await manager.EraseSectorsAsync(actualLun, partition.FirstLBA, sectorCount);
            eraseStopwatch.Stop();

            Logging.Log($"Successfully erased partition '{partitionName}' in {eraseStopwatch.Elapsed.TotalSeconds:F2}s.");

            return 0;
        });
    }
}