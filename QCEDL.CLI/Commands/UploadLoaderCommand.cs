using System.CommandLine;
using Microsoft.Extensions.Logging;
using QCEDL.CLI.Core;
using QCEDL.CLI.Logging;

namespace QCEDL.CLI.Commands;

internal sealed class UploadLoaderCommand(
    ILogger<UploadLoaderCommand> logger,
    GlobalOptionsBinder globalOptionsBinder,
    IEdlManagerProvider edlManagerProvider) : ICommand
{
    public Command Create()
    {
        var command = new Command(
            "upload-loader",
            "Connects in Sahara mode and uploads the specified Firehose loader (--loader). Does not proceed to Firehose operations.");

        command.SetHandler(
            ExecuteAsync,
            globalOptionsBinder);

        return command;
    }

    private async Task<int> ExecuteAsync(GlobalOptionsBinder globalOptions)
    {
        logger.ExecutingUploadLoader();

        if (string.IsNullOrEmpty(globalOptions.LoaderPath))
        {
            logger.LoaderOptionMissing();
            return 1;
        }

        try
        {
            using var manager = edlManagerProvider.CreateEdlManager();
            var currentMode = await manager.DetectCurrentModeAsync();
            switch (currentMode)
            {
                case DeviceMode.Sahara:
                    logger.SaharaModeDetected();
                    await manager.UploadLoaderViaSaharaAsync();
                    logger.LoaderUploadCompleted();
                    break;
                case DeviceMode.Firehose:
                    logger.AlreadyInFirehose();
                    return 1;
                case DeviceMode.Unknown:
                case DeviceMode.Error:
                default:
                    logger.CannotUploadLoaderUnknownMode(currentMode);
                    return 1;
            }
        }
        catch (FileNotFoundException ex)
        {
            logger.ExceptedException(ex);
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            logger.ExceptedException(ex);
            return 1;
        }
        catch (ArgumentException ex)
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