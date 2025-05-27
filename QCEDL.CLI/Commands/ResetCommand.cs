using System.CommandLine;
using Microsoft.Extensions.Logging;
using QCEDL.CLI.Core;
using QCEDL.CLI.Logging;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace QCEDL.CLI.Commands;

internal sealed class ResetCommand(
    ILogger<ResetCommand> logger,
    GlobalOptionsBinder globalOptionsBinder,
    IEdlManagerProvider edlManagerProvider) : ICommand
{
    private static readonly Option<PowerValue> ModeOption = new(
        aliases: ["--mode", "-m"],
        description: "Specify the reset mode.",
        getDefaultValue: () => PowerValue.reset
    );


    private static readonly Option<uint> DelayOption = new(
        aliases: ["--delay", "-d"],
        description: "Delay in seconds before executing the power command.",
        getDefaultValue: () => 1
    );

    public Command Create()
    {
        var command = new Command("reset", "Resets or powers off the device using Firehose.")
        {
            ModeOption,
            DelayOption
        };

        command.SetHandler(
            ExecuteAsync,
            globalOptionsBinder,
            ModeOption,
            DelayOption);

        return command;
    }

    private async Task<int> ExecuteAsync(
        GlobalOptionsBinder globalOptions,
        PowerValue powerMode,
        uint delayInSeconds)
    {
        logger.ExecutingReset(powerMode, delayInSeconds);

        try
        {
            using var manager = edlManagerProvider.CreateEdlManager();
            await manager.EnsureFirehoseModeAsync();

            logger.AttemptingPowerCommand(powerMode, delayInSeconds);

            var success = await Task.Run(() => manager.Firehose.Reset(powerMode, delayInSeconds));

            if (success)
            {
                logger.PowerCommandSucceeded(powerMode);

                switch (powerMode)
                {
                    case PowerValue.reset or PowerValue.edl:
                        logger.DeviceResetting();
                        break;
                    case PowerValue.off:
                        logger.DevicePoweringOff();
                        break;
                }
            }
            else
            {
                logger.PowerCommandFailed(powerMode);
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
        catch (Exception ex)
        {
            logger.UnexceptedException(ex);
            return 1;
        }

        return 0;
    }
}