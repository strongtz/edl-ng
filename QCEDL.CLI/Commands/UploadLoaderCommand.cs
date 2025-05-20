using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;
using System.CommandLine;

namespace QCEDL.CLI.Commands
{
    internal class UploadLoaderCommand
    {
        public static Command Create(GlobalOptionsBinder globalOptionsBinder)
        {
            var command = new Command("upload-loader", "Connects in Sahara mode and uploads the specified Firehose loader (--loader). Does not proceed to Firehose operations.")
            {
                // No specific options for this command itself
            };

            command.SetHandler(ExecuteAsync, globalOptionsBinder);

            return command;
        }

        private static async Task<int> ExecuteAsync(GlobalOptionsBinder globalOptions)
        {
            Logging.Log("Executing 'upload-loader' command...", LogLevel.Trace);

            if (string.IsNullOrEmpty(globalOptions.LoaderPath))
            {
                Logging.Log("Error: The '--loader' option is required for the 'upload-loader' command.", LogLevel.Error);
                return 1;
            }

            try
            {
                using var manager = new EdlManager(globalOptions);
                DeviceMode currentMode = await manager.DetectCurrentModeAsync();
                switch (currentMode)
                {
                    case DeviceMode.Sahara:
                        Logging.Log("Device detected in Sahara mode. Proceeding with loader upload...", LogLevel.Info);
                        await manager.UploadLoaderViaSaharaAsync();
                        Logging.Log("Loader upload process completed. Device should restart or re-enumerate.", LogLevel.Debug);
                        break;
                    case DeviceMode.Firehose:
                        Logging.Log("Error: Device is already in Firehose mode. Cannot upload loader.", LogLevel.Error);
                        return 1;
                    case DeviceMode.Unknown:
                    case DeviceMode.Error:
                    default:
                        Logging.Log($"Error: Cannot upload loader. Device mode is {currentMode} or could not be reliably determined.", LogLevel.Error);
                        return 1;
                }
            }
            catch (FileNotFoundException ex)
            {
                Logging.Log(ex.Message, LogLevel.Error);
                return 1;
            }
            catch (InvalidOperationException ex)
            {
                Logging.Log($"Operation Error: {ex.Message}", LogLevel.Error);
                return 1;
            }
            catch (ArgumentException ex)
            {
                Logging.Log($"Configuration Error: {ex.Message}", LogLevel.Error);
                return 1;
            }
            catch (Exception ex)
            {
                Logging.Log($"An unexpected error occurred: {ex.Message}", LogLevel.Error);
                Logging.Log(ex.ToString(), LogLevel.Debug);
                return 1;
            }

            return 0;
        }
    }
}
