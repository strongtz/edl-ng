using System.CommandLine;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using QCEDL.CLI.Core;
using QCEDL.CLI.Logging;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.JSON.StorageInfo;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace QCEDL.CLI.Commands;

internal sealed class EraseSectorCommand(
    ILogger<EraseSectorCommand> logger,
    GlobalOptionsBinder globalOptionsBinder,
    IEdlManagerProvider edlManagerProvider) : ICommand
{
    private static readonly Argument<ulong> StartSectorArgument =
        new("start_sector", "The starting sector LBA to erase from.");

    private static readonly Argument<ulong> SectorsArgument = new("sectors", "The number of sectors to erase.");

    private static readonly Option<uint> LunOption = new(
        aliases: ["--lun", "-u"],
        description: "Specify the LUN number to erase from.",
        getDefaultValue: () => 0);
    
    public Command Create()
    {
        var command =
            new Command("erase-sector", "Erases a specified number of sectors from a given LUN and start LBA.")
            {
                StartSectorArgument, SectorsArgument, LunOption
            };
        
        command.SetHandler(
            ExecuteAsync,
            globalOptionsBinder,
            StartSectorArgument,
            SectorsArgument,
            LunOption);

        return command;
    }

    private async Task<int> ExecuteAsync(
        GlobalOptionsBinder globalOptions,
        ulong startSectorUlong,
        ulong sectorsToEraseUlong,
        uint lun)
    {
        logger.ExecutingEraseSectorCommand(lun, startSectorUlong, sectorsToEraseUlong);
        var commandStopwatch = Stopwatch.StartNew();

        if (sectorsToEraseUlong == 0)
        {
            logger.InvalidSectorCount();
            return 1;
        }

        if (startSectorUlong > uint.MaxValue || sectorsToEraseUlong > uint.MaxValue ||
            startSectorUlong + sectorsToEraseUlong - 1 > uint.MaxValue)
        {
            logger.SectorRangeExceedsUintMax(startSectorUlong, sectorsToEraseUlong);
            return 1;
        }

        var startSector = (uint)startSectorUlong;
        var numSectorsToErase = (uint)sectorsToEraseUlong;

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
                logger.StorageInfoUnavailable(lun, storageType, storageEx);
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
                logger.DefaultSectorSizeWarning(storageType, sectorSize);
            }

            logger.UsingSectorSize(sectorSize, lun);

            logger.AttemptEraseSectors(numSectorsToErase, startSector, lun);
            var eraseStopwatch = Stopwatch.StartNew();

            var success = await Task.Run(() => manager.Firehose.Erase(
                storageType,
                lun,
                globalOptions.Slot,
                sectorSize,
                startSector,
                numSectorsToErase
            ));
            eraseStopwatch.Stop();

            if (success)
            {
                logger.EraseSectorsSucceeded(numSectorsToErase, eraseStopwatch.Elapsed);
            }
            else
            {
                logger.EraseSectorsFailed();
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
            logger.EraseSectorCommandFinished(commandStopwatch.Elapsed);
        }

        return 0;
    }
}