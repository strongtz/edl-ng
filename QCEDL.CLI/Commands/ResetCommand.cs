using System.CommandLine;
using Microsoft.Extensions.Logging;
using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace QCEDL.CLI.Commands;

internal sealed class ResetCommand
{
    private static readonly Option<PowerValue> ModeOption = new(
        aliases: ["--mode", "-m"],
        description: "Specify the reset mode.",
        getDefaultValue: () => PowerValue.Reset
    );


    private static readonly Option<uint> DelayOption = new(
        aliases: ["--delay", "-d"],
        description: "Delay in seconds before executing the power command.",
        getDefaultValue: () => 1
    );

    public static Command Create(GlobalOptionsBinder globalOptionsBinder)
    {
        var command = new Command("reset", "Resets or powers off the device using Firehose.")
        {
            ModeOption,
            DelayOption
        };

        command.SetHandler(ExecuteAsync,
            globalOptionsBinder,
            ModeOption,
            DelayOption);

        return command;
    }

    private static async Task<int> ExecuteAsync(
        GlobalOptionsBinder globalOptions,
        PowerValue powerMode,
        uint delayInSeconds)
    {
        Logging.Log($"Executing 'reset' command: Mode '{powerMode}', Delay '{delayInSeconds}s'...", LogLevel.Trace);

        try
        {
            using var manager = new EdlManager(globalOptions);
            await manager.EnsureFirehoseModeAsync();

            Logging.Log($"Attempting to send power command: Mode '{powerMode}', Delay '{delayInSeconds}s'...");

            var success = await Task.Run(() => manager.Firehose.Reset(powerMode, delayInSeconds));

            if (success)
            {
                Logging.Log($"Power command '{powerMode}' sent successfully.");
                if (powerMode is PowerValue.Reset or PowerValue.Edl)
                {
                    Logging.Log("Device should now be resetting.");
                }
                else if (powerMode == PowerValue.Off)
                {
                    Logging.Log("Device should now be powering off.");
                }
            }
            else
            {
                Logging.Log($"Failed to send power command '{powerMode}'. Check previous logs for NAK or errors.", LogLevel.Error);
                return 1;
            }
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
        catch (InvalidOperationException ex)
        {
            Logging.Log($"Operation Error: {ex.Message}", LogLevel.Error);
            return 1;
        }
        catch (Exception ex)
        {
            Logging.Log($"An unexpected error occurred in 'reset': {ex.Message}", LogLevel.Error);
            Logging.Log(ex.ToString(), LogLevel.Debug);
            return 1;
        }

        return 0;
    }
}