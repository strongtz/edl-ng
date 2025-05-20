using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;
using QCEDL.NET.PartitionTable;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;
using System.CommandLine;

namespace QCEDL.CLI.Commands
{
    internal class WritePartitionCommand
    {
        private static readonly Argument<string> PartitionNameArgument = new("partition_name", "The name of the partition to write.");
        private static readonly Argument<FileInfo> FilenameArgument =
            new("filename", "The file containing data to write to the partition.")
            { Arity = ArgumentArity.ExactlyOne };

        private static readonly Option<uint?> LunOption = new Option<uint?>(
            aliases: ["--lun", "-u"],
            description: "Specify the LUN number. If not specified, all LUNs will be scanned for the partition.");

        public static Command Create(GlobalOptionsBinder globalOptionsBinder)
        {
            var command = new Command("write-part", "Writes data from a file to a partition by name.")
            {
                PartitionNameArgument,
                FilenameArgument,
                LunOption
            };

            FilenameArgument.ExistingOnly();

            command.SetHandler(ExecuteAsync,
                globalOptionsBinder,
                PartitionNameArgument,
                FilenameArgument,
                LunOption);

            return command;
        }

        private static async Task<int> ExecuteAsync(
            GlobalOptionsBinder globalOptions,
            string partitionName,
            FileInfo inputFile,
            uint? specifiedLun)
        {
            Logging.Log($"Executing 'write-part' command: Partition '{partitionName}', File '{inputFile.FullName}'...", LogLevel.Trace);

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

                GPTPartition? foundPartition = null;
                uint actualLun = 0;
                uint actualSectorSize = 0;

                List<uint> lunsToScan = [];
                if (specifiedLun.HasValue)
                {
                    lunsToScan.Add(specifiedLun.Value);
                    Logging.Log($"Scanning specified LUN: {specifiedLun.Value}", LogLevel.Debug);
                }
                else
                {
                    Logging.Log("No LUN specified, attempting to determine number of LUNs and scan all.", LogLevel.Debug);
                    Qualcomm.EmergencyDownload.Layers.APSS.Firehose.JSON.StorageInfo.Root? devInfo = null;
                    try
                    {
                        devInfo = await Task.Run(() => manager.Firehose.GetStorageInfo(storageType, 0)); // Check LUN 0 for num_physical
                    }
                    catch (Exception ex)
                    {
                        Logging.Log($"Could not get device info to determine LUN count from LUN 0. Error: {ex.Message}. Will try a default range.", LogLevel.Warning);
                    }

                    if (devInfo?.storage_info?.num_physical > 0)
                    {
                        for (uint i = 0; i < devInfo.storage_info.num_physical; i++) lunsToScan.Add(i);
                        Logging.Log($"Device reports {devInfo.storage_info.num_physical} LUN(s). Scanning LUNs: {string.Join(", ", lunsToScan)}", LogLevel.Debug);
                    }
                    else
                    {
                        lunsToScan.AddRange(new uint[] { 0, 1, 2, 3, 4, 5 });
                        Logging.Log($"Could not determine LUN count. Scanning default LUNs: {string.Join(", ", lunsToScan)}", LogLevel.Warning);
                    }
                }

                foreach (uint currentLun in lunsToScan)
                {
                    Logging.Log($"Scanning LUN {currentLun} for partition '{partitionName}'...", LogLevel.Debug);
                    Qualcomm.EmergencyDownload.Layers.APSS.Firehose.JSON.StorageInfo.Root? lunStorageInfo = null;
                    try
                    {
                        lunStorageInfo = await Task.Run(() => manager.Firehose.GetStorageInfo(storageType, currentLun));
                    }
                    catch (Exception storageEx)
                    {
                        Logging.Log($"Could not get storage info for LUN {currentLun}. Error: {storageEx.Message}. Skipping LUN.", LogLevel.Warning);
                        continue;
                    }

                    uint currentSectorSize = lunStorageInfo?.storage_info?.block_size > 0 ? (uint)lunStorageInfo.storage_info.block_size : 0;
                    if (currentSectorSize == 0)
                    {
                        currentSectorSize = storageType switch { StorageType.NVME => 512, StorageType.SDCC => 512, _ => 4096 };
                        Logging.Log($"Storage info for LUN {currentLun} unreliable, using default sector size for {storageType}: {currentSectorSize}", LogLevel.Warning);
                    }
                    Logging.Log($"Using sector size: {currentSectorSize} bytes for LUN {currentLun}.", LogLevel.Debug);

                    uint sectorsForGptRead = 64;
                    byte[] gptData;
                    try
                    {
                        gptData = await Task.Run(() => manager.Firehose.Read(storageType, currentLun, currentSectorSize, 0, sectorsForGptRead - 1));
                    }
                    catch (Exception readEx)
                    {
                        Logging.Log($"Failed to read GPT area from LUN {currentLun}. Error: {readEx.Message}. Skipping LUN.", LogLevel.Warning);
                        continue;
                    }

                    if (gptData == null || gptData.Length < currentSectorSize * 2)
                    {
                        Logging.Log($"Failed to read sufficient data for GPT from LUN {currentLun}.", LogLevel.Warning);
                        continue;
                    }

                    using var stream = new MemoryStream(gptData);
                    try
                    {
                        var gpt = GPT.ReadFromStream(stream, (int)currentSectorSize);
                        if (gpt != null)
                        {
                            foreach (var p in gpt.Partitions)
                            {
                                string currentPartitionName = new string(p.Name).TrimEnd('\0');
                                if (currentPartitionName.Equals(partitionName, StringComparison.OrdinalIgnoreCase))
                                {
                                    foundPartition = p;
                                    actualLun = currentLun;
                                    actualSectorSize = currentSectorSize;
                                    Logging.Log($"Found partition '{partitionName}' on LUN {actualLun} with sector size {actualSectorSize}.", LogLevel.Info);
                                    Logging.Log($"  Details - Type: {p.TypeGUID}, UID: {p.UID}, LBA: {p.FirstLBA}-{p.LastLBA}", LogLevel.Debug);
                                    break;
                                }
                            }
                        }
                    }
                    catch (InvalidDataException) { Logging.Log($"No valid GPT found or parse error on LUN {currentLun}.", LogLevel.Debug); }
                    catch (Exception ex) { Logging.Log($"Error processing GPT on LUN {currentLun}: {ex.Message}", LogLevel.Warning); }

                    if (foundPartition.HasValue) break;
                }

                if (!foundPartition.HasValue)
                {
                    Logging.Log($"Error: Partition '{partitionName}' not found on " + (specifiedLun.HasValue ? $"LUN {specifiedLun.Value}." : "any scanned LUN."), LogLevel.Error);
                    return 1;
                }

                byte[] originalData = await File.ReadAllBytesAsync(inputFile.FullName);
                if (originalData.Length == 0)
                {
                    Logging.Log("Error: Input file is empty. Nothing to write.", LogLevel.Error);
                    return 1;
                }

                byte[] dataToWrite;
                long originalLength = originalData.Length;
                long remainder = originalLength % actualSectorSize;
                if (remainder != 0)
                {
                    long paddedLength = originalLength + (actualSectorSize - remainder);
                    Logging.Log($"Input file size ({originalLength} bytes) is not a multiple of partition's sector size ({actualSectorSize} bytes). Padding with zeros to {paddedLength} bytes.", LogLevel.Warning);
                    dataToWrite = new byte[paddedLength];
                    Buffer.BlockCopy(originalData, 0, dataToWrite, 0, (int)originalLength);
                    // The rest of dataToWrite will be initialized to 0 by default.
                }
                else
                {
                    dataToWrite = originalData;
                }
                ulong partitionSizeInBytes = (foundPartition.Value.LastLBA - foundPartition.Value.FirstLBA + 1) * actualSectorSize;
                if ((ulong)dataToWrite.Length > partitionSizeInBytes)
                {
                    Logging.Log($"Error: Padded data size ({dataToWrite.Length} bytes) is larger than the partition '{partitionName}' size ({partitionSizeInBytes} bytes). Original file size was {originalLength} bytes.", LogLevel.Error);
                    return 1;
                }
                uint numSectorsToWrite = (uint)(dataToWrite.Length / actualSectorSize);
                Logging.Log($"Data size to write: {dataToWrite.Length} bytes. Will write {numSectorsToWrite} sectors to partition '{partitionName}'.", LogLevel.Debug);
                if ((ulong)dataToWrite.Length < partitionSizeInBytes)
                {
                    Logging.Log($"Warning: Padded data size is smaller than partition size. The remaining space in partition '{partitionName}' will not be explicitly overwritten or zeroed out by this operation.", LogLevel.Warning);
                }
                ulong partStartSector = foundPartition.Value.FirstLBA;
                if (partStartSector > uint.MaxValue)
                {
                    Logging.Log($"Error: Partition start LBA ({partStartSector}) exceeds uint.MaxValue, not supported by current Firehose.Program.", LogLevel.Error);
                    return 1;
                }
                Logging.Log($"Attempting to write {dataToWrite.Length} bytes to partition '{partitionName}' (LUN {actualLun}, LBA {partStartSector})...", LogLevel.Info);

                bool success = await Task.Run(() => manager.Firehose.Program(
                    storageType,
                    actualLun,
                    actualSectorSize,
                    (uint)partStartSector,
                    inputFile.Name, // filename for XML attribute
                    dataToWrite
                ));

                if (success)
                {
                    Logging.Log($"Data successfully written to partition '{partitionName}'.", LogLevel.Info);
                }
                else
                {
                    Logging.Log($"Failed to write data to partition '{partitionName}'. Check previous logs.", LogLevel.Error);
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
                Logging.Log($"An unexpected error occurred in 'write-part': {ex.Message}", LogLevel.Error);
                Logging.Log(ex.ToString(), LogLevel.Debug);
                return 1;
            }
            return 0;
        }
    }
}
