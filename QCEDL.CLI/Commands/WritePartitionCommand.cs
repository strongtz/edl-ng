using System.CommandLine;
using System.Diagnostics;
using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;

namespace QCEDL.CLI.Commands;

internal sealed class WritePartitionCommand
{
    private static readonly Argument<string> PartitionNameArgument =
        new("partition_name", "The name of the partition to write.");

    private static readonly Argument<FileInfo> FilenameArgument =
        new("filename", "The file containing data to write to the partition.")
        {
            Arity = ArgumentArity.ExactlyOne
        };

    private static readonly Option<uint?> LunOption = new(
        aliases: ["--lun", "-u"],
        description: "Specify the LUN number. If not specified, all LUNs will be scanned for the partition.");

    public static Command Create(GlobalOptionsBinder globalOptionsBinder)
    {
        var command = new Command("write-part", "Writes data from a file to a partition by name.")
        {
            PartitionNameArgument, FilenameArgument, LunOption
        };

        _ = FilenameArgument.ExistingOnly();

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
        FileInfo inputFile,
        uint? specifiedLun)
    {
        Logging.Log($"Executing 'write-part' command: Partition '{partitionName}', File '{inputFile.FullName}'...", LogLevel.Trace);
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

            var partitionInfo = await manager.FindPartitionWithLunAsync(partitionName, specifiedLun);
            if (!partitionInfo.HasValue)
            {
                Logging.Log($"Error: Partition '{partitionName}' not found on {(specifiedLun.HasValue ? $"LUN {specifiedLun.Value}" : "any scanned LUN")}.", LogLevel.Error);
                return 1;
            }

            var (partition, actualLun) = partitionInfo.Value;
            var geometry = await manager.GetStorageGeometryAsync(actualLun);
            var sectorSize = geometry.SectorSize;

            var partitionSizeInBytes = (partition.LastLBA - partition.FirstLBA + 1) * sectorSize;
            var originalFileLength = inputFile.Length;

            Logging.Log($"Found partition '{partitionName}': LBA {partition.FirstLBA}-{partition.LastLBA}, Size: {partitionSizeInBytes / (1024.0 * 1024.0):F2} MiB");

            if (originalFileLength > (long)partitionSizeInBytes)
            {
                Logging.Log($"Error: Input file size ({originalFileLength} bytes) exceeds partition size ({partitionSizeInBytes} bytes).", LogLevel.Error);
                return 1;
            }

            var paddedBytes = AlignmentHelper.AlignTo((ulong)originalFileLength, sectorSize);
            if (paddedBytes > partitionSizeInBytes)
            {
                Logging.Log($"Error: Padded data size ({paddedBytes} bytes) exceeds partition '{partitionName}' size ({partitionSizeInBytes} bytes).", LogLevel.Error);
                return 1;
            }

            if (originalFileLength < (long)partitionSizeInBytes)
            {
                Logging.Log($"Warning: Input data ({originalFileLength} bytes) is smaller than partition size ({partitionSizeInBytes} bytes). Remaining space will be untouched.", LogLevel.Warning);
            }

            var targetDescription = manager.IsHostDeviceMode
                ? "host device"
                : manager.IsRadxaWosMode
                    ? "Radxa WoS platform"
                    : $"LUN {actualLun}";
            Logging.Log($"Writing partition '{partitionName}' ({paddedBytes} bytes) to {targetDescription} starting at LBA {partition.FirstLBA}...");

            long bytesWrittenReported = 0;
            var writeStopwatch = Stopwatch.StartNew();

            void ProgressAction(long current, long total)
            {
                bytesWrittenReported = current;
                var percentage = total == 0 ? 100 : current * 100.0 / total;
                var elapsed = writeStopwatch.Elapsed;
                var speed = elapsed.TotalSeconds > 0 ? current / elapsed.TotalSeconds : 0;
                var speedStr = "N/A";
                if (elapsed.TotalSeconds > 0.1)
                {
                    speedStr = speed > 1024 * 1024 ? $"{speed / (1024 * 1024):F2} MiB/s" :
                        speed > 1024 ? $"{speed / 1024:F2} KiB/s" :
                        $"{speed:F0} B/s";
                }
                Console.Write($"\rWriting: {percentage:F1}% ({current / (1024.0 * 1024.0):F2} / {total / (1024.0 * 1024.0):F2} MiB) [{speedStr}]      ");
            }

            await using var fileStream = inputFile.OpenRead();
            await manager.WriteSectorsFromStreamAsync(
                actualLun,
                partition.FirstLBA,
                fileStream,
                fileStream.Length,
                padToSector: true,
                inputFile.Name,
                ProgressAction);

            writeStopwatch.Stop();
            Console.WriteLine();

            if (bytesWrittenReported == 0 && paddedBytes > 0)
            {
                bytesWrittenReported = (long)Math.Min(paddedBytes, long.MaxValue);
            }

            Logging.Log($"Successfully wrote {bytesWrittenReported / (1024.0 * 1024.0):F2} MiB to partition '{partitionName}' in {writeStopwatch.Elapsed.TotalSeconds:F2}s.");
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
        catch (PlatformNotSupportedException ex)
        {
            Logging.Log($"Platform Error: {ex.Message}", LogLevel.Error);
            return 1;
        }
        catch (Exception ex)
        {
            Logging.Log($"An unexpected error occurred in 'write-part': {ex.Message}", LogLevel.Error);
            Logging.Log(ex.ToString(), LogLevel.Debug);
            return 1;
        }
        finally
        {
            commandStopwatch.Stop();
            Logging.Log($"'write-part' command finished in {commandStopwatch.Elapsed.TotalSeconds:F2}s.", LogLevel.Debug);
        }

        return 0;
    }

}