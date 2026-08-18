using System.CommandLine;
using System.Diagnostics;
using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;

namespace QCEDL.CLI.Commands;

internal sealed class EraseAllCommand
{
    public static Command Create(GlobalOptionsBinder globalOptionsBinder)
    {
        var command = new Command("erase-all", "Erases every physical partition on the Firehose storage device.");

        command.SetHandler(ExecuteAsync, globalOptionsBinder);

        return command;
    }

    private static async Task<int> ExecuteAsync(GlobalOptionsBinder globalOptions)
    {
        Logging.Log("Executing 'erase-all' command...", LogLevel.Trace);

        return await CommandExecutor.RunAsync("erase-all", async () =>
        {
            using var manager = new EdlManager(globalOptions);

            Logging.Log(
                "Erasing every physical partition on the device. All data, including boot partitions, will be lost.",
                LogLevel.Warning);

            var eraseStopwatch = Stopwatch.StartNew();
            await manager.EraseAllAsync();
            eraseStopwatch.Stop();

            Logging.Log(
                $"Successfully erased all physical partitions in {eraseStopwatch.Elapsed.TotalSeconds:F2}s.");
            return 0;
        });
    }
}
