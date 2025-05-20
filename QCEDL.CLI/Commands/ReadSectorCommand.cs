using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;
using System.CommandLine;

namespace QCEDL.CLI.Commands
{
    internal class ReadSectorCommand
    {
        private static readonly Argument<ulong> StartSectorArgument = new("start_sector", "The starting sector LBA to read from.");
        private static readonly Argument<ulong> SectorsArgument = new("sectors", "The number of sectors to read.");
        private static readonly Argument<FileInfo> FilenameArgument = new("filename", "The file to save the read data to.") { Arity = ArgumentArity.ExactlyOne };

        private static readonly Option<uint> LunOption = new Option<uint>(
            aliases: ["--lun", "-u"],
            description: "Specify the LUN number to read from.",
            getDefaultValue: () => 0);

        public static Command Create(GlobalOptionsBinder globalOptionsBinder)
        {
            var command = new Command("read-sector", "Reads a specified number of sectors from a given LUN and start LBA, saving to a file.")
            {
                StartSectorArgument,
                SectorsArgument,
                FilenameArgument,
                LunOption // Command-specific LUN
            };

            command.SetHandler(ExecuteAsync,
                globalOptionsBinder,
                StartSectorArgument,
                SectorsArgument,
                FilenameArgument,
                LunOption);

            return command;
        }

        private static async Task<int> ExecuteAsync(
            GlobalOptionsBinder globalOptions,
            ulong startSector,
            ulong sectorsToRead,
            FileInfo outputFile,
            uint lun)
        {
            Logging.Log($"Executing 'read-sector' command: LUN {lun}, Start LBA {startSector}, Sectors {sectorsToRead}, File '{outputFile.FullName}'...", LogLevel.Trace);

            if (sectorsToRead == 0)
            {
                Logging.Log("Error: Number of sectors to read must be greater than 0.", LogLevel.Error);
                return 1;
            }

            try
            {
                using var manager = new EdlManager(globalOptions);
                await manager.EnsureFirehoseModeAsync();
                await manager.ConfigureFirehoseAsync();

                StorageType storageType = globalOptions.MemoryType ?? StorageType.UFS;
                Logging.Log($"Using storage type: {storageType}", LogLevel.Debug);

                // Determine sector size
                Qualcomm.EmergencyDownload.Layers.APSS.Firehose.JSON.StorageInfo.Root? storageInfo = null;
                try
                {
                    storageInfo = await Task.Run(() => manager.Firehose.GetStorageInfo(storageType, lun));
                }
                catch (Exception storageEx)
                {
                    Logging.Log($"Could not get storage info for LUN {lun} (StorageType: {storageType}). Using default sector size. Error: {storageEx.Message}", LogLevel.Warning);
                }

                uint sectorSize = storageInfo?.storage_info?.block_size > 0 ? (uint)storageInfo.storage_info.block_size : 0;
                if (sectorSize == 0) // Fallback if GetStorageInfo failed or returned 0
                {
                    sectorSize = storageType switch
                    {
                        StorageType.NVME => 512,
                        StorageType.SDCC => 512,
                        _ => 4096,
                    };
                    Logging.Log($"Storage info unreliable or unavailable, using default sector size for {storageType}: {sectorSize}", LogLevel.Warning);
                }
                Logging.Log($"Using sector size: {sectorSize} bytes for LUN {lun}.", LogLevel.Debug);

                if (startSector > uint.MaxValue || (startSector + sectorsToRead - 1) > uint.MaxValue)
                {
                    Logging.Log($"Error: Sector range exceeds uint.MaxValue, which is not supported by the current Firehose.Read implementation.", LogLevel.Error);
                    return 1;
                }

                uint firstLba = (uint)startSector;
                uint lastLba = (uint)(startSector + sectorsToRead - 1);

                Logging.Log($"Attempting to read {sectorsToRead} sectors (LBA {firstLba} to {lastLba}) from LUN {lun}...", LogLevel.Info);

                byte[] data = await Task.Run(() => manager.Firehose.Read(
                    storageType,
                    lun,
                    sectorSize,
                    firstLba,
                    lastLba
                ));

                if (data == null || data.Length == 0)
                {
                    Logging.Log("Failed to read data or no data returned.", LogLevel.Error);
                    return 1;
                }

                Logging.Log($"Successfully read {data.Length} bytes. Writing to '{outputFile.FullName}'...", LogLevel.Info);
                await File.WriteAllBytesAsync(outputFile.FullName, data);
                Logging.Log("Data written to file successfully.", LogLevel.Info);

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
            catch (IOException ex)
            {
                Logging.Log($"IO Error (e.g., writing file): {ex.Message}", LogLevel.Error);
                return 1;
            }
            catch (Exception ex)
            {
                Logging.Log($"An unexpected error occurred in 'read-sector': {ex.Message}", LogLevel.Error);
                Logging.Log(ex.ToString(), LogLevel.Debug);
                return 1;
            }

            return 0;
        }
    }
}
