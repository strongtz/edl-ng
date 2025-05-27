using System.CommandLine;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using QCEDL.CLI.Core;
using QCEDL.CLI.Logging;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.JSON.StorageInfo;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace QCEDL.CLI.Commands;

internal sealed class ReadSectorCommand(
    ILogger<ReadSectorCommand> logger,
    GlobalOptionsBinder globalOptionsBinder,
    IEdlManagerProvider edlManagerProvider) : ICommand
{
    private static readonly Argument<ulong> StartSectorArgument =
        new("start_sector", "The starting sector LBA to read from.");

    private static readonly Argument<ulong> SectorsArgument = new("sectors", "The number of sectors to read.");

    private static readonly Argument<FileInfo> FilenameArgument =
        new("filename", "The file to save the read data to.") { Arity = ArgumentArity.ExactlyOne };

    private static readonly Option<uint> LunOption = new(
        aliases: ["--lun", "-u"],
        description: "Specify the LUN number to read from.",
        getDefaultValue: () => 0);

    public Command Create()
    {
        var command =
            new Command("read-sector",
                "Reads a specified number of sectors from a given LUN and start LBA, saving to a file.")
            {
                StartSectorArgument, SectorsArgument, FilenameArgument, LunOption // Command-specific LUN
            };

        command.SetHandler(
            ExecuteAsync,
            globalOptionsBinder,
            StartSectorArgument,
            SectorsArgument,
            FilenameArgument,
            LunOption);

        return command;
    }

    private async Task<int> ExecuteAsync(
        GlobalOptionsBinder globalOptions,
        ulong startSector,
        ulong sectorsToRead,
        FileInfo outputFile,
        uint lun)
    {
        logger.ExecutingReadSector(lun, startSector, sectorsToRead, outputFile.FullName);
        var commandStopwatch = Stopwatch.StartNew();

        if (sectorsToRead == 0)
        {
            logger.SectorsCountMustBeGreaterThanZero();
            return 1;
        }

        try
        {
            using var manager = edlManagerProvider.CreateEdlManager();
            await manager.EnsureFirehoseModeAsync();
            await manager.ConfigureFirehoseAsync();

            var storageType = globalOptions.MemoryType ?? StorageType.UFS;
            logger.UsingStorageType(storageType);

            // Determine sector size
            Root? storageInfo = null;
            try
            {
                storageInfo =
                    await Task.Run(() => manager.Firehose.GetStorageInfo(storageType, lun, globalOptions.Slot));
            }
            catch (Exception storageEx)
            {
                logger.GetStorageInfoFailed(lun, storageType, storageEx);
            }

            var sectorSize = storageInfo?.StorageInfo?.BlockSize > 0 ? (uint)storageInfo.StorageInfo.BlockSize : 0;
            if (sectorSize == 0) // Fallback if GetStorageInfo failed or returned 0
            {
                sectorSize = storageType switch
                {
                    StorageType.NVME => 512,
                    StorageType.SDCC => 512,
                    _ => 4096,
                };
                logger.DefaultSectorSizeApplied(storageType, sectorSize);
            }

            logger.UsingSectorSize(sectorSize, lun);

            if (startSector > uint.MaxValue || startSector + sectorsToRead - 1 > uint.MaxValue)
            {
                logger.SectorRangeNotSupported();
                return 1;
            }

            var firstLba = (uint)startSector;
            var lastLba = (uint)(startSector + sectorsToRead - 1);
            var totalBytesToRead = (long)sectorsToRead * sectorSize;

            if (totalBytesToRead <= 0)
            {
                logger.ExecutingReadSector(lun, startSector, sectorsToRead, outputFile.FullName);
                await File.WriteAllBytesAsync(outputFile.FullName, []);
                return 0;
            }

            logger.PreparingReadSector(sectorsToRead, firstLba, lastLba, totalBytesToRead, lun, outputFile.FullName);

            long bytesReadReported = 0;
            var readStopwatch = new Stopwatch();

            Action<long, long> progressAction = (current, total) =>
            {
                bytesReadReported = current;
                var percentage = total == 0 ? 100 : current * 100.0 / total;
                var elapsed = readStopwatch.Elapsed;
                var speed = current / elapsed.TotalSeconds;
                var speedStr = "N/A";
                if (elapsed.TotalSeconds > 0.1)
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
                outputFile.Directory?.Create();
                await using var fileStream = outputFile.Open(FileMode.Create, FileAccess.Write, FileShare.None);

                readStopwatch.Start();
                success = await Task.Run(() => manager.Firehose.ReadToStream(
                    storageType,
                    lun,
                    globalOptions.Slot,
                    sectorSize,
                    firstLba,
                    lastLba,
                    fileStream,
                    progressAction
                ));
                readStopwatch.Stop();
            }
            catch (IOException ioEx)
            {
                logger.IoErrorWritingFile(outputFile.FullName, ioEx);
                Console.WriteLine();
                return 1;
            }

            Console.WriteLine(); // Newline after progress bar

            if (!success)
            {
                logger.ReadSectorFailed();
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

            logger.ReadSectorSucceeded(bytesReadReported / (1024.0 * 1024.0), outputFile.FullName,
                readStopwatch.Elapsed);
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
            logger.ReadSectorFinished(commandStopwatch.Elapsed.TotalSeconds);
        }

        return 0;
    }
}