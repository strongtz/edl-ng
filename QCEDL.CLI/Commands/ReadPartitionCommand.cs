using System.CommandLine;
using System.Diagnostics;
using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;

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
                Logging.Log($"Warning: Partition '{partitionName}' has zero size. Nothing to read.", LogLevel.Warning);
                await File.WriteAllBytesAsync(outputFile.FullName, []);
                return 0;
            }

            var geometry = await manager.GetStorageGeometryAsync(actualLun);
            var sectorSize = geometry.SectorSize;
            var totalBytesDecimal = (decimal)sectorCount * sectorSize;
            var totalBytes = totalBytesDecimal > long.MaxValue ? long.MaxValue : (long)totalBytesDecimal;

            var targetDescription = manager.IsHostDeviceMode
                ? "host device"
                : manager.IsRadxaWosMode
                    ? "Radxa WoS platform"
                    : $"LUN {actualLun}";
            Logging.Log($"Reading partition '{partitionName}' ({targetDescription}, LBA {partition.FirstLBA}-{partition.LastLBA}, {totalBytesDecimal / (1024.0m * 1024.0m):F2} MiB) into '{outputFile.FullName}'...");

            long bytesReadReported = 0;
            var readStopwatch = new Stopwatch();
            void ProgressAction(long current, long total)
            {
                bytesReadReported = current;
                var percentage = total == 0 ? 100 : current * 100.0 / total;
                var elapsed = readStopwatch.Elapsed;
                var speed = elapsed.TotalSeconds > 0 ? current / elapsed.TotalSeconds : 0;
                var speedStr = "N/A";
                if (elapsed.TotalSeconds > 0.1)
                {
                    speedStr = speed > 1024 * 1024 ? $"{speed / (1024 * 1024):F2} MiB/s" :
                        speed > 1024 ? $"{speed / 1024:F2} KiB/s" :
                        $"{speed:F0} B/s";
                }
                Console.Write($"\rReading: {percentage:F1}% ({current / (1024.0 * 1024.0):F2} / {total / (1024.0 * 1024.0):F2} MiB) [{speedStr}]      ");
            }

            try
            {
                outputFile.Directory?.Create();
                using var fileStream = outputFile.Open(FileMode.Create, FileAccess.Write, FileShare.None);

                readStopwatch.Start();
                await manager.ReadSectorsToStreamAsync(actualLun, partition.FirstLBA, sectorCount, fileStream, ProgressAction);
                readStopwatch.Stop();
            }
            catch (IOException ioEx)
            {
                Logging.Log($"IO Error creating/writing to file '{outputFile.FullName}': {ioEx.Message}", LogLevel.Error);
                Console.WriteLine();
                TryDeletePartialFile(outputFile);
                return 1;
            }
            catch (Exception ex)
            {
                Logging.Log($"Error reading partition '{partitionName}': {ex.Message}", LogLevel.Error);
                Logging.Log(ex.ToString(), LogLevel.Debug);
                Console.WriteLine();
                TryDeletePartialFile(outputFile);
                return 1;
            }

            Console.WriteLine();

            if (bytesReadReported == 0 && totalBytes > 0)
            {
                bytesReadReported = totalBytes;
            }

            Logging.Log($"Successfully read partition '{partitionName}' ({bytesReadReported / (1024.0 * 1024.0):F2} MiB) into '{outputFile.FullName}' in {readStopwatch.Elapsed.TotalSeconds:F2}s.");

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

    private static void TryDeletePartialFile(FileInfo file)
    {
        try
        {
            if (file.Exists)
            {
                file.Delete();
            }
        }
        catch (Exception ex)
        {
            Logging.Log($"Could not delete partial file '{file.FullName}': {ex.Message}", LogLevel.Warning);
        }
    }
}