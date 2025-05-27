using System.CommandLine;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using QCEDL.CLI.Core;
using QCEDL.CLI.Logging;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.JSON.StorageInfo;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace QCEDL.CLI.Commands;

internal sealed class WriteSectorCommand(
    ILogger<WriteSectorCommand> logger,
    GlobalOptionsBinder globalOptionsBinder,
    IEdlManagerProvider edlManagerProvider) : ICommand
{
    private static readonly Argument<ulong> StartSectorArgument =
        new("start_sector", "The starting sector LBA to write to.");

    private static readonly Argument<FileInfo> FilenameArgument =
        new("filename", "The file containing data to write.") { Arity = ArgumentArity.ExactlyOne };

    private static readonly Option<uint> LunOption = new(
        aliases: ["--lun", "-u"],
        description: "Specify the LUN number to write to.",
        getDefaultValue: () => 0);

    static WriteSectorCommand()
    {
        FilenameArgument.ExistingOnly();
    }

    public Command Create()
    {
        var command =
            new Command(
                "write-sector",
                "Writes data from a file to a specified number of sectors from a given LUN and start LBA.")
            {
                StartSectorArgument, FilenameArgument, LunOption
            };

        command.SetHandler(
            ExecuteAsync,
            globalOptionsBinder,
            StartSectorArgument,
            FilenameArgument,
            LunOption);

        return command;
    }

    private async Task<int> ExecuteAsync(
        GlobalOptionsBinder globalOptions,
        ulong startSector,
        FileInfo inputFile,
        uint lun)
    {
        logger.ExecutingWriteSector(lun, startSector, inputFile.FullName);
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

            Root? storageInfo = null;
            try
            {
                storageInfo =
                    await Task.Run(() => manager.Firehose.GetStorageInfo(storageType, lun, globalOptions.Slot));
            }
            catch (Exception storageEx)
            {
                logger.CouldNotGetStorageInfo(lun, storageType, storageEx);
            }

            var sectorSize = storageInfo?.StorageInfo?.BlockSize > 0 ? (uint)storageInfo.StorageInfo.BlockSize : 0;
            if (sectorSize == 0)
            {
                sectorSize = storageType switch
                {
                    StorageType.NVME => 512,
                    StorageType.SDCC => 512,
                    _ => 4096,
                };
                logger.StorageInfoUnreliable(storageType, sectorSize);
            }

            logger.UsingSectorSize(sectorSize, lun);

            var originalFileLength = inputFile.Length;
            long totalBytesToWriteIncludingPadding;
            uint numSectorsForXml;

            var remainder = originalFileLength % sectorSize;
            if (remainder != 0)
            {
                totalBytesToWriteIncludingPadding = originalFileLength + (sectorSize - remainder);
                logger.InputFilePaddingWarning(originalFileLength, sectorSize, totalBytesToWriteIncludingPadding);
            }
            else
            {
                totalBytesToWriteIncludingPadding = originalFileLength;
            }

            numSectorsForXml = (uint)(totalBytesToWriteIncludingPadding / sectorSize);

            logger.DataToWriteWithPadding(originalFileLength, totalBytesToWriteIncludingPadding, numSectorsForXml);

            if (startSector > uint.MaxValue)
            {
                logger.StartSectorLbaExceedsMaxValue(startSector);
                return 1;
            }

            logger.AttemptingSectorWrite(numSectorsForXml, totalBytesToWriteIncludingPadding, lun, startSector);

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
                    lun,
                    globalOptions.Slot,
                    sectorSize,
                    (uint)startSector,
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
                logger.IoErrorReadingInputFile(inputFile.FullName, ioEx);
                Console.WriteLine();
                return 1;
            }

            Console.WriteLine(); // Newline after progress bar

            if (success)
            {
                logger.DataWrittenToSectors(bytesWrittenReported / (1024.0 * 1024.0), writeStopwatch.Elapsed);
            }
            else
            {
                logger.FailedToWriteToSectors();
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

        return 0;
    }
}