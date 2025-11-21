using System.CommandLine;
using System.Diagnostics;
using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;

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

        try
        {
            using var manager = new EdlManager(globalOptions);
            var isDirectMode = manager.IsHostDeviceMode || manager.IsRadxaWosMode;

            var effectiveLun = isDirectMode ? 0u : lun;
            var geometry = await manager.GetStorageGeometryAsync(effectiveLun);
            var targetDescription = manager.IsHostDeviceMode
                ? "host device"
                : manager.IsRadxaWosMode
                    ? "Radxa WoS platform"
                    : $"LUN {effectiveLun}";
            Logging.Log($"Using sector size: {geometry.SectorSize} bytes for {targetDescription}.", LogLevel.Debug);

            Logging.Log($"Attempting to erase {sectorsToEraseUlong} sectors starting at LBA {startSectorUlong} on {targetDescription}...");
            var eraseStopwatch = Stopwatch.StartNew();

            await manager.EraseSectorsAsync(effectiveLun, startSectorUlong, sectorsToEraseUlong);

            eraseStopwatch.Stop();
            Logging.Log($"Successfully erased {sectorsToEraseUlong} sectors in {eraseStopwatch.Elapsed.TotalSeconds:F2}s.");
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