using System.CommandLine;
using System.Diagnostics;
using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.JSON.StorageInfo;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace QCEDL.CLI.Commands;

internal sealed class EraseSectorCommand
{
    private static readonly Argument<ulong> StartSectorArgument = new("start_sector", "The starting sector LBA to erase from.");
    private static readonly Argument<ulong> SectorsArgument = new("sectors", "The number of sectors to erase.");

    private static readonly Option<uint> LunOption = new(
        aliases: ["--lun", "-u"],
        description: "Specify the LUN number to erase from.",
        getDefaultValue: () => 0);

    public static Command Create(GlobalOptionsBinder globalOptionsBinder)
    {
        var command = new Command("erase-sector", "Erases a specified number of sectors from a given LUN and start LBA.")
        {
            StartSectorArgument,
            SectorsArgument,
            LunOption
        };

        command.SetHandler(ExecuteAsync,
            globalOptionsBinder,
            StartSectorArgument,
            SectorsArgument,
            LunOption);

        return command;
    }

    private static async Task<int> ExecuteAsync(
        GlobalOptionsBinder globalOptions,
        ulong startSectorUlong,
        ulong sectorsToEraseUlong,
        uint lun)
    {
        Logging.Log($"Executing 'erase-sector' command: LUN {lun}, Start LBA {startSectorUlong}, Sectors {sectorsToEraseUlong}...", LogLevel.Trace);
        var commandStopwatch = Stopwatch.StartNew();

        if (sectorsToEraseUlong == 0)
        {
            Logging.Log("Error: Number of sectors to erase must be greater than 0.", LogLevel.Error);
            return 1;
        }

        if (startSectorUlong > uint.MaxValue || sectorsToEraseUlong > uint.MaxValue || startSectorUlong + sectorsToEraseUlong - 1 > uint.MaxValue)
        {
            Logging.Log($"Error: Sector range (Start: {startSectorUlong}, Count: {sectorsToEraseUlong}) exceeds uint.MaxValue, which is not supported by the current Firehose.Erase implementation.", LogLevel.Error);
            return 1;
        }

        var startSector = (uint)startSectorUlong;
        var numSectorsToErase = (uint)sectorsToEraseUlong;

        try
        {
            using var manager = new EdlManager(globalOptions);
            await manager.EnsureFirehoseModeAsync();
            await manager.ConfigureFirehoseAsync();

            var storageType = globalOptions.MemoryType ?? StorageType.Ufs;
            Logging.Log($"Using storage type: {storageType}", LogLevel.Debug);

            Root? storageInfo = null;
            try
            {
                storageInfo = await Task.Run(() => manager.Firehose.GetStorageInfo(storageType, lun, globalOptions.Slot));
            }
            catch (Exception storageEx)
            {
                Logging.Log($"Could not get storage info for LUN {lun} (StorageType: {storageType}). Using default sector size. Error: {storageEx.Message}", LogLevel.Warning);
            }

            var sectorSize = storageInfo?.StorageInfo?.BlockSize > 0 ? (uint)storageInfo.StorageInfo.BlockSize : 0;
            if (sectorSize == 0)
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

            Logging.Log($"Attempting to erase {numSectorsToErase} sectors starting at LBA {startSector} on LUN {lun}...");
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
                Logging.Log($"Successfully erased {numSectorsToErase} sectors in {eraseStopwatch.Elapsed.TotalSeconds:F2}s.");
            }
            else
            {
                Logging.Log("Failed to erase sectors. Check previous logs for NAK or errors.", LogLevel.Error);
                return 1;
            }
        }
        catch (FileNotFoundException ex) { Logging.Log(ex.Message, LogLevel.Error); return 1; }
        catch (ArgumentException ex) { Logging.Log(ex.Message, LogLevel.Error); return 1; }
        catch (InvalidOperationException ex) { Logging.Log($"Operation Error: {ex.Message}", LogLevel.Error); return 1; }
        catch (IOException ex) { Logging.Log($"IO Error: {ex.Message}", LogLevel.Error); return 1; }
        catch (Exception ex)
        {
            Logging.Log($"An unexpected error occurred in 'erase-sector': {ex.Message}", LogLevel.Error);
            Logging.Log(ex.ToString(), LogLevel.Debug);
            return 1;
        }
        finally
        {
            commandStopwatch.Stop();
            Logging.Log($"'erase-sector' command finished in {commandStopwatch.Elapsed.TotalSeconds:F2}s.", LogLevel.Debug);
        }
        return 0;
    }
}