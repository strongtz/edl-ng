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

        if (sectorsToEraseUlong == 0)
        {
            Logging.Log("Error: Number of sectors to erase must be greater than 0.", LogLevel.Error);
            return 1;
        }

        return await CommandExecutor.RunAsync("erase-sector", async () =>
        {
            using var manager = new EdlManager(globalOptions);
            var effectiveLun = manager.IsDirectMode ? 0u : lun;
            var geometry = await manager.GetStorageGeometryAsync(effectiveLun);
            var targetDescription = manager.GetTargetDescription(effectiveLun);
            Logging.Log($"Using sector size: {geometry.SectorSize} bytes for {targetDescription}.", LogLevel.Debug);

            Logging.Log($"Attempting to erase {sectorsToEraseUlong} sectors starting at LBA {startSectorUlong} on {targetDescription}...");
            var eraseStopwatch = Stopwatch.StartNew();

            await manager.EraseSectorsAsync(effectiveLun, startSectorUlong, sectorsToEraseUlong);

            eraseStopwatch.Stop();
            Logging.Log($"Successfully erased {sectorsToEraseUlong} sectors in {eraseStopwatch.Elapsed.TotalSeconds:F2}s.");

            return 0;
        });
    }
}