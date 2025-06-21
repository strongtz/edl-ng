using System.CommandLine;
using System.Diagnostics;
using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;
using QCEDL.NET.PartitionTable;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.JSON.StorageInfo;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace QCEDL.CLI.Commands;

internal sealed class ErasePartitionCommand
{
    private static readonly Argument<string> PartitionNameArgument = new("partition_name", "The name of the partition to erase.");
    private static readonly Option<uint?> LunOption = new(
        aliases: ["--lun", "-u"],
        description: "Specify the LUN number. If not specified, all LUNs will be scanned for the partition.");

    public static Command Create(GlobalOptionsBinder globalOptionsBinder)
    {
        var command = new Command("erase-part", "Erases a partition by name from the device.")
        {
            PartitionNameArgument,
            LunOption
        };

        command.SetHandler(ExecuteAsync,
            globalOptionsBinder,
            PartitionNameArgument,
            LunOption);

        return command;
    }

    private static async Task<int> ExecuteAsync(
        GlobalOptionsBinder globalOptions,
        string partitionName,
        uint? specifiedLun)
    {
        Logging.Log($"Executing 'erase-part' command: Partition '{partitionName}'...", LogLevel.Trace);
        var commandStopwatch = Stopwatch.StartNew();

        try
        {
            using var manager = new EdlManager(globalOptions);
            await manager.EnsureFirehoseModeAsync();
            await manager.ConfigureFirehoseAsync();

            var storageType = globalOptions.MemoryType ?? StorageType.Ufs;
            Logging.Log($"Using storage type: {storageType}", LogLevel.Debug);

            GptPartition? foundPartition = null;
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
                Root? devInfo = null;
                try
                {
                    devInfo = await Task.Run(() => manager.Firehose.GetStorageInfo(storageType, 0, globalOptions.Slot)); // Check LUN 0 for num_physical
                }
                catch (Exception ex)
                {
                    Logging.Log($"Could not get device info to determine LUN count from LUN 0. Error: {ex.Message}. Will try a default range.", LogLevel.Warning);
                }

                if (devInfo?.StorageInfo?.NumPhysical > 0)
                {
                    for (uint i = 0; i < devInfo.StorageInfo.NumPhysical; i++)
                    {
                        lunsToScan.Add(i);
                    }

                    Logging.Log($"Device reports {devInfo.StorageInfo.NumPhysical} LUN(s). Scanning LUNs: {string.Join(", ", lunsToScan)}", LogLevel.Debug);
                }
                else
                {
                    if (storageType == StorageType.Spinor)
                    {
                        lunsToScan.Add(0);
                    }
                    else
                    {
                        lunsToScan.AddRange([0, 1, 2, 3, 4, 5]);
                        Logging.Log($"Could not determine LUN count. Scanning default LUNs: {string.Join(", ", lunsToScan)}", LogLevel.Warning);
                    }
                }
            }

            foreach (var currentLun in lunsToScan)
            {
                Logging.Log($"Scanning LUN {currentLun} for partition '{partitionName}'...", LogLevel.Debug);
                Root? lunStorageInfo = null;
                try
                {
                    lunStorageInfo = await Task.Run(() => manager.Firehose.GetStorageInfo(storageType, currentLun, globalOptions.Slot));
                }
                catch (Exception storageEx)
                {
                    Logging.Log($"Could not get storage info for LUN {currentLun}. Error: {storageEx.Message}. Skipping LUN.", LogLevel.Warning);
                    continue;
                }

                var currentSectorSize = lunStorageInfo?.StorageInfo?.BlockSize > 0 ? (uint)lunStorageInfo.StorageInfo.BlockSize : 0;
                if (currentSectorSize == 0)
                {
                    currentSectorSize = storageType switch
                    {
                        StorageType.Nvme => 512,
                        StorageType.Sdcc => 512,
                        StorageType.Spinor or StorageType.Ufs or StorageType.Nand or _ => 4096,
                    };
                    Logging.Log($"Storage info for LUN {currentLun} unreliable, using default sector size for {storageType}: {currentSectorSize}", LogLevel.Warning);
                }
                Logging.Log($"Using sector size: {currentSectorSize} bytes for LUN {currentLun}.", LogLevel.Debug);

                uint sectorsForGptRead = 64;
                byte[]? gptData;
                try
                {
                    gptData = await Task.Run(() => manager.Firehose.Read(storageType, currentLun, globalOptions.Slot, currentSectorSize, 0, sectorsForGptRead - 1));
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
                    var gpt = Gpt.ReadFromStream(stream, (int)currentSectorSize);
                    if (gpt != null)
                    {
                        foreach (var p in gpt.Partitions)
                        {
                            var currentPartitionName = p.GetName();
                            if (currentPartitionName.Equals(partitionName, StringComparison.OrdinalIgnoreCase))
                            {
                                foundPartition = p; actualLun = currentLun; actualSectorSize = currentSectorSize;
                                Logging.Log($"Found partition '{partitionName}' on LUN {actualLun} with sector size {actualSectorSize}.");
                                Logging.Log($"  Details - Type: {p.TypeGUID}, UID: {p.UID}, LBA: {p.FirstLBA}-{p.LastLBA}", LogLevel.Debug);
                                break;
                            }
                        }
                    }
                }
                catch (InvalidDataException) { Logging.Log($"No valid GPT found or parse error on LUN {currentLun}.", LogLevel.Debug); }
                catch (Exception ex) { Logging.Log($"Error processing GPT on LUN {currentLun}: {ex.Message}", LogLevel.Warning); }

                if (foundPartition.HasValue)
                {
                    break;
                }
            }

            if (!foundPartition.HasValue)
            {
                Logging.Log($"Error: Partition '{partitionName}' not found on " + (specifiedLun.HasValue ? $"LUN {specifiedLun.Value}." : "any scanned LUN."), LogLevel.Error);
                return 1;
            }

            var partStartSectorUlong = foundPartition.Value.FirstLBA;
            var partLastSectorUlong = foundPartition.Value.LastLBA;
            var numSectorsToEraseUlong = partLastSectorUlong - partStartSectorUlong + 1;

            if (partStartSectorUlong > uint.MaxValue || numSectorsToEraseUlong > uint.MaxValue || partStartSectorUlong + numSectorsToEraseUlong - 1 > uint.MaxValue)
            {
                Logging.Log($"Error: Partition '{partitionName}' sector range (Start: {partStartSectorUlong}, Count: {numSectorsToEraseUlong}) exceeds uint.MaxValue, which is not supported by the current Firehose.Erase implementation.", LogLevel.Error);
                return 1;
            }
            var startSector = (uint)partStartSectorUlong;
            var numSectorsToErase = (uint)numSectorsToEraseUlong;

            if (numSectorsToErase == 0)
            {
                Logging.Log($"Warning: Partition '{partitionName}' has zero size. Nothing to erase.", LogLevel.Warning);
                return 0;
            }

            Logging.Log($"Attempting to erase partition '{partitionName}' (LUN {actualLun}, LBA {startSector}, {numSectorsToErase} sectors)...");
            var eraseStopwatch = Stopwatch.StartNew();

            var success = await Task.Run(() => manager.Firehose.Erase(
                storageType,
                actualLun,
                globalOptions.Slot,
                actualSectorSize,
                startSector,
                numSectorsToErase
            ));
            eraseStopwatch.Stop();

            if (success)
            {
                Logging.Log($"Successfully erased partition '{partitionName}' in {eraseStopwatch.Elapsed.TotalSeconds:F2}s.");
            }
            else
            {
                Logging.Log($"Failed to erase partition '{partitionName}'. Check previous logs.", LogLevel.Error);
                return 1;
            }
        }
        catch (FileNotFoundException ex) { Logging.Log(ex.Message, LogLevel.Error); return 1; }
        catch (ArgumentException ex) { Logging.Log(ex.Message, LogLevel.Error); return 1; }
        catch (InvalidOperationException ex) { Logging.Log($"Operation Error: {ex.Message}", LogLevel.Error); return 1; }
        catch (IOException ex) { Logging.Log($"IO Error: {ex.Message}", LogLevel.Error); return 1; }
        catch (Exception ex)
        {
            Logging.Log($"An unexpected error occurred in 'erase-part': {ex.Message}", LogLevel.Error);
            Logging.Log(ex.ToString(), LogLevel.Debug);
            return 1;
        }
        finally
        {
            commandStopwatch.Stop();
            Logging.Log($"'erase-part' command finished in {commandStopwatch.Elapsed.TotalSeconds:F2}s.", LogLevel.Debug);
        }
        return 0;
    }
}