using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;
using System.CommandLine;

namespace QCEDL.CLI.Commands
{
    internal class WriteSectorCommand
    {
        private static readonly Argument<ulong> StartSectorArgument = new("start_sector", "The starting sector LBA to write to.");
        private static readonly Argument<FileInfo> FilenameArgument =
            new("filename", "The file containing data to write.")
            { Arity = ArgumentArity.ExactlyOne };

        private static readonly Option<uint> LunOption = new Option<uint>(
            aliases: ["--lun", "-u"],
            description: "Specify the LUN number to write to.",
            getDefaultValue: () => 0);

        public static Command Create(GlobalOptionsBinder globalOptionsBinder)
        {
            var command = new Command("write-sector", "Writes data from a file to a specified number of sectors from a given LUN and start LBA.")
            {
                StartSectorArgument,
                FilenameArgument,
                LunOption
            };

            FilenameArgument.ExistingOnly();

            command.SetHandler(ExecuteAsync,
                globalOptionsBinder,
                StartSectorArgument,
                FilenameArgument,
                LunOption);

            return command;
        }

        private static async Task<int> ExecuteAsync(
            GlobalOptionsBinder globalOptions,
            ulong startSector,
            FileInfo inputFile,
            uint lun)
        {
            Logging.Log($"Executing 'write-sector' command: LUN {lun}, Start LBA {startSector}, File '{inputFile.FullName}'...", LogLevel.Trace);

            if (!inputFile.Exists)
            {
                Logging.Log($"Error: Input file '{inputFile.FullName}' not found.", LogLevel.Error);
                return 1;
            }

            try
            {
                using var manager = new EdlManager(globalOptions);
                await manager.EnsureFirehoseModeAsync();
                await manager.ConfigureFirehoseAsync();

                StorageType storageType = globalOptions.MemoryType ?? StorageType.UFS;
                Logging.Log($"Using storage type: {storageType}", LogLevel.Debug);

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
                if (sectorSize == 0)
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

                byte[] originalData = await File.ReadAllBytesAsync(inputFile.FullName);
                if (originalData.Length == 0)
                {
                    Logging.Log("Error: Input file is empty. Nothing to write.", LogLevel.Error);
                    return 1;
                }

                byte[] dataToWrite;
                long originalLength = originalData.Length;
                long remainder = originalLength % sectorSize;

                if (remainder != 0)
                {
                    long paddedLength = originalLength + (sectorSize - remainder);
                    Logging.Log($"Input file size ({originalLength} bytes) is not a multiple of sector size ({sectorSize} bytes). Padding with zeros to {paddedLength} bytes.", LogLevel.Warning);
                    dataToWrite = new byte[paddedLength];
                    Buffer.BlockCopy(originalData, 0, dataToWrite, 0, (int)originalLength);
                    // The rest of dataToWrite will be initialized to 0 by default.
                }
                else
                {
                    dataToWrite = originalData;
                }

                uint numSectorsToWrite = (uint)(dataToWrite.Length / sectorSize);
                Logging.Log($"Data size to write: {dataToWrite.Length} bytes, Sectors to write: {numSectorsToWrite}", LogLevel.Debug);

                if (startSector > uint.MaxValue)
                {
                    Logging.Log($"Error: Start sector LBA ({startSector}) exceeds uint.MaxValue, which is not supported by the current Firehose.Program implementation's start_sector parameter.", LogLevel.Error);
                    return 1;
                }

                Logging.Log($"Attempting to write {numSectorsToWrite} sectors ({dataToWrite.Length} bytes) to LUN {lun}, starting at LBA {startSector}...", LogLevel.Info);

                bool success = await Task.Run(() => manager.Firehose.Program(
                    storageType,
                    lun,
                    sectorSize,
                    (uint)startSector,
                    inputFile.Name,
                    dataToWrite
                ));

                if (success)
                {
                    Logging.Log("Data written to sectors successfully.", LogLevel.Info);
                }
                else
                {
                    Logging.Log("Failed to write data to sectors. Check previous logs for NAK or errors.", LogLevel.Error);
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
            catch (IOException ex)
            {
                Logging.Log($"IO Error (e.g., reading input file): {ex.Message}", LogLevel.Error);
                return 1;
            }
            catch (Exception ex)
            {
                Logging.Log($"An unexpected error occurred in 'write-sector': {ex.Message}", LogLevel.Error);
                Logging.Log(ex.ToString(), LogLevel.Debug);
                return 1;
            }

            return 0;
        }
    }
}
