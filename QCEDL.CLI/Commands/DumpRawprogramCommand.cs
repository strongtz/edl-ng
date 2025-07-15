using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;
using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;
using QCEDL.NET.PartitionTable;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.JSON.StorageInfo;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace QCEDL.CLI.Commands;

internal sealed class DumpRawprogramCommand
{
    private static readonly Argument<DirectoryInfo> DumpSaveDirArgument = new("dump_save_dir", "The directory to save partition files and rawprogram XML.") { Arity = ArgumentArity.ExactlyOne };

    private static readonly Option<uint> LunOption = new(
        aliases: ["--lun", "-u"],
        description: "Specify the LUN number to dump partitions from.",
        getDefaultValue: () => 0);

    public static Command Create(GlobalOptionsBinder globalOptionsBinder)
    {
        var command = new Command("dump-rawprogram", "Reads all partitions to individual files from a certain LUN and generates rawprogram XML file.")
        {
            DumpSaveDirArgument,
            LunOption
        };

        command.SetHandler(ExecuteAsync,
            globalOptionsBinder,
            DumpSaveDirArgument,
            LunOption);

        return command;
    }

    private static async Task<int> ExecuteAsync(
        GlobalOptionsBinder globalOptions,
        DirectoryInfo dumpSaveDir,
        uint lun)
    {
        Logging.Log($"Executing 'dump-rawprogram' command: LUN {lun}, Save Directory '{dumpSaveDir.FullName}'...", LogLevel.Trace);
        var commandStopwatch = Stopwatch.StartNew();

        try
        {
            using var manager = new EdlManager(globalOptions);
            await manager.EnsureFirehoseModeAsync();
            await manager.ConfigureFirehoseAsync();

            var storageType = globalOptions.MemoryType ?? StorageType.Ufs;
            Logging.Log($"Using storage type: {storageType}", LogLevel.Debug);

            // Get storage info to determine sector size
            Root? storageInfo = null;
            try
            {
                storageInfo = await Task.Run(() => manager.Firehose.GetStorageInfo(storageType, lun, globalOptions.Slot));
            }
            catch (Exception storageEx)
            {
                Logging.Log($"Could not get storage info for LUN {lun} (StorageType: {storageType}). Using default sector size. Error: {storageEx.Message}", LogLevel.Warning);
            }

            var sectorSize = storageInfo?.StorageInfo?.BlockSize > 0 ? (uint)storageInfo.StorageInfo.BlockSize : 0;
            if (sectorSize == 0) // Fallback if GetStorageInfo failed or returned 0
            {
                sectorSize = storageType switch
                {
                    StorageType.Nvme => 512,
                    StorageType.Sdcc => 512,
                    StorageType.Spinor or StorageType.Ufs or StorageType.Nand or _ => 4096,
                };
                Logging.Log($"Storage info unreliable or unavailable, using default sector size for {storageType}: {sectorSize}", LogLevel.Warning);
            }
            Logging.Log($"Using sector size: {sectorSize} bytes for LUN {lun}.", LogLevel.Debug);

            // Create save directory if it doesn't exist
            dumpSaveDir.Create();

            // Read GPT to get partition information
            Logging.Log($"Reading GPT from LUN {lun}...");

            // Read enough sectors to contain GPT header and partition entries
            // Reading 64 sectors is usually safe for primary GPT header + entries
            uint sectorsForGptRead = 64;
            byte[]? gptData;
            try
            {
                gptData = await Task.Run(() => manager.Firehose.Read(
                    storageType,
                    lun,
                    globalOptions.Slot,
                    sectorSize,
                    0, // Start sector for GPT
                    sectorsForGptRead - 1 // Last sector for GPT
                ));
            }
            catch (Exception readEx)
            {
                Logging.Log($"Failed to read GPT area from LUN {lun}. Error: {readEx.Message}", LogLevel.Error);
                return 1;
            }

            if (gptData == null || gptData.Length < sectorSize * 2)
            {
                Logging.Log($"Failed to read sufficient data for GPT from LUN {lun}.", LogLevel.Error);
                return 1;
            }

            Gpt? gpt;
            using (var stream = new MemoryStream(gptData))
            {
                try
                {
                    gpt = Gpt.ReadFromStream(stream, (int)sectorSize);
                }
                catch (InvalidDataException)
                {
                    Logging.Log($"No valid GPT found on LUN {lun}.", LogLevel.Error);
                    return 1;
                }
                catch (Exception ex)
                {
                    Logging.Log($"Error processing GPT on LUN {lun}: {ex.Message}", LogLevel.Error);
                    return 1;
                }
            }

            if (gpt == null)
            {
                Logging.Log($"No valid GPT found on LUN {lun}.", LogLevel.Error);
                return 1;
            }

            // Calculate GPT sizes based on FirstUsableLBA and LastUsableLBA
            var mainGptSectors = gpt.Header.FirstUsableLBA; // From sector 0 to FirstUsableLBA-1
            var totalBlocks = (ulong)(storageInfo?.StorageInfo?.TotalBlocks ?? 0);
            var backupGptSectors = totalBlocks > 0 ? totalBlocks - gpt.Header.LastUsableLBA - 1 : 0; // From LastUsableLBA+1 to TotalBlocks-1

            Logging.Log($"GPT header indicates FirstUsableLBA: {gpt.Header.FirstUsableLBA}, LastUsableLBA: {gpt.Header.LastUsableLBA}", LogLevel.Debug);
            Logging.Log($"Calculated main GPT size: {mainGptSectors} sectors (0 to {gpt.Header.FirstUsableLBA - 1})", LogLevel.Debug);
            Logging.Log($"Calculated backup GPT size: {backupGptSectors} sectors ({gpt.Header.LastUsableLBA + 1} to {totalBlocks - 1})", LogLevel.Debug);

            // Save main GPT (from sector 0 to FirstUsableLBA-1)
            var mainGptFileName = $"gpt_main{lun}.bin";
            var mainGptFilePath = Path.Combine(dumpSaveDir.FullName, mainGptFileName);
            try
            {
                var mainGptData = await Task.Run(() => manager.Firehose.Read(
                    storageType,
                    lun,
                    globalOptions.Slot,
                    sectorSize,
                    0, // Start sector
                    (uint)(mainGptSectors - 1) // Last sector (FirstUsableLBA-1)
                ));

                if (mainGptData != null && mainGptData.Length >= sectorSize)
                {
                    await File.WriteAllBytesAsync(mainGptFilePath, mainGptData);
                    Logging.Log($"Saved main GPT to '{mainGptFilePath}' ({mainGptSectors} sectors)", LogLevel.Info);
                }
                else
                {
                    Logging.Log("Main GPT data insufficient, skipping main GPT save.", LogLevel.Warning);
                }
            }
            catch (Exception ex)
            {
                Logging.Log($"Error reading/saving main GPT: {ex.Message}", LogLevel.Error);
                return 1;
            }

            // Try to read backup GPT (from LastUsableLBA+1 to TotalBlocks-1)
            byte[]? backupGptData = null;
            var backupGptFileName = $"gpt_backup{lun}.bin";
            var backupGptFilePath = Path.Combine(dumpSaveDir.FullName, backupGptFileName);

            if (backupGptSectors > 0 && gpt.Header.LastUsableLBA + 1 < totalBlocks)
            {
                try
                {
                    Logging.Log($"Reading backup GPT from LBA {gpt.Header.LastUsableLBA + 1} to {totalBlocks - 1}...", LogLevel.Debug);
                    backupGptData = await Task.Run(() => manager.Firehose.Read(
                        storageType,
                        lun,
                        globalOptions.Slot,
                        sectorSize,
                        (uint)(gpt.Header.LastUsableLBA + 1), // Backup GPT start sector
                        (uint)(totalBlocks - 1) // Backup GPT end sector
                    ));

                    if (backupGptData != null && backupGptData.Length >= sectorSize)
                    {
                        await File.WriteAllBytesAsync(backupGptFilePath, backupGptData);
                        Logging.Log($"Saved backup GPT to '{backupGptFilePath}' ({backupGptSectors} sectors)", LogLevel.Info);
                    }
                    else
                    {
                        Logging.Log("Backup GPT data insufficient, skipping backup GPT save.", LogLevel.Warning);
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log($"Error reading/saving backup GPT: {ex.Message}", LogLevel.Warning);
                }
            }
            else
            {
                Logging.Log("No backup GPT area available (LastUsableLBA+1 >= TotalBlocks).", LogLevel.Debug);
            }

            var partitions = gpt.Partitions.Where(p => !string.IsNullOrWhiteSpace(p.GetName().TrimEnd('\0'))).ToList();
            Logging.Log($"Found {partitions.Count} partitions on LUN {lun}.", LogLevel.Info);

            if (partitions.Count == 0)
            {
                Logging.Log("No partitions found to dump.", LogLevel.Warning);
                return 0;
            }

            // Create rawprogram XML document
            var rawprogramDoc = new XDocument(
                new XElement("data",
                    new XAttribute("SECTOR_SIZE_IN_BYTES", sectorSize.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("physical_partition_number", lun.ToString(CultureInfo.InvariantCulture))
                )
            );

            // Add main GPT to rawprogram XML
            var mainGptElement = new XElement("program",
                new XAttribute("filename", mainGptFileName),
                new XAttribute("label", "gpt_main"),
                new XAttribute("start_sector", "0"),
                new XAttribute("num_partition_sectors", mainGptSectors.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("SECTOR_SIZE_IN_BYTES", sectorSize.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("physical_partition_number", lun.ToString(CultureInfo.InvariantCulture))
            );
            rawprogramDoc.Root!.Add(mainGptElement);

            // Add backup GPT to rawprogram XML if it exists
            if (backupGptData != null && backupGptSectors > 0)
            {
                var backupGptElement = new XElement("program",
                    new XAttribute("filename", backupGptFileName),
                    new XAttribute("label", "gpt_backup"),
                    new XAttribute("start_sector", $"NUM_DISK_SECTORS-{backupGptSectors}."),
                    new XAttribute("num_partition_sectors", backupGptSectors.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("SECTOR_SIZE_IN_BYTES", sectorSize.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("physical_partition_number", lun.ToString(CultureInfo.InvariantCulture))
                );
                rawprogramDoc.Root.Add(backupGptElement);
            }

            var successfulDumps = 0;
            var totalPartitions = partitions.Count;

            // Count GPT files as successful dumps
            successfulDumps++; // Main GPT
            if (backupGptData != null && backupGptSectors > 0)
            {
                successfulDumps++; // Backup GPT
            }

            foreach (var partition in partitions)
            {
                var partitionName = partition.GetName().TrimEnd('\0');
                if (string.IsNullOrWhiteSpace(partitionName))
                {
                    Logging.Log("Skipping partition with empty name.", LogLevel.Debug);
                    continue;
                }

                var partStartSector = partition.FirstLBA;
                var partLastSector = partition.LastLBA;
                var numSectorsToRead = partLastSector - partStartSector + 1;

                if (partStartSector > uint.MaxValue || partLastSector > uint.MaxValue)
                {
                    Logging.Log($"Warning: Partition '{partitionName}' sector range (LBA {partStartSector}-{partLastSector}) exceeds uint.MaxValue. Skipping.", LogLevel.Warning);
                    continue;
                }

                var totalBytesToRead = (long)numSectorsToRead * sectorSize;
                if (totalBytesToRead <= 0)
                {
                    Logging.Log($"Warning: Partition '{partitionName}' has zero or negative size. Skipping.", LogLevel.Warning);
                    continue;
                }

                // Create safe filename
                var safeFileName = CreateSafeFileName(partitionName);
                var partitionFilePath = Path.Combine(dumpSaveDir.FullName, safeFileName);

                Logging.Log($"Dumping partition '{partitionName}' ({numSectorsToRead} sectors, {totalBytesToRead / (1024.0 * 1024.0):F2} MiB) to '{partitionFilePath}'...", LogLevel.Debug);

                long bytesReadReported = 0;
                var readStopwatch = new Stopwatch();

                void ProgressAction(long current, long total)
                {
                    bytesReadReported = current;
                    var percentage = total == 0 ? 100 : current * 100.0 / total;
                    var elapsed = readStopwatch.Elapsed;
                    var speed = current / elapsed.TotalSeconds;
                    var speedStr = "N/A";
                    if (elapsed.TotalSeconds > 0.1)
                    {
                        speedStr = speed > 1024 * 1024 ? $"{speed / (1024 * 1024):F2} MiB/s" :
                            speed > 1024 ? $"{speed / 1024:F2} KiB/s" :
                            $"{speed:F0} B/s";
                    }
                    Console.Write($"\rDumping {partitionName}: {percentage:F1}% ({current / (1024.0 * 1024.0):F2} / {total / (1024.0 * 1024.0):F2} MiB) [{speedStr}]      ");
                }

                bool success;
                try
                {
                    using var fileStream = File.Open(partitionFilePath, FileMode.Create, FileAccess.Write, FileShare.None);

                    readStopwatch.Start();
                    success = await Task.Run(() => manager.Firehose.ReadToStream(
                        storageType,
                        lun,
                        globalOptions.Slot,
                        sectorSize,
                        (uint)partStartSector,
                        (uint)partLastSector,
                        fileStream,
                        ProgressAction
                    ));
                    readStopwatch.Stop();
                }
                catch (Exception ex)
                {
                    Logging.Log($"Error dumping partition '{partitionName}': {ex.Message}", LogLevel.Error);
                    Console.WriteLine();
                    continue;
                }

                Console.WriteLine(); // Newline after progress bar

                if (!success)
                {
                    Logging.Log($"Failed to dump partition '{partitionName}'.", LogLevel.Error);
                    try
                    {
                        if (File.Exists(partitionFilePath))
                        {
                            File.Delete(partitionFilePath);
                        }
                    }
                    catch (Exception ex) { Logging.Log($"Could not delete failed dump file '{partitionFilePath}': {ex.Message}", LogLevel.Warning); }
                    continue;
                }

                Logging.Log($"Successfully dumped partition '{partitionName}' ({bytesReadReported / (1024.0 * 1024.0):F2} MiB) in {readStopwatch.Elapsed.TotalSeconds:F2}s.", LogLevel.Debug);

                // Add to rawprogram XML
                var programElement = new XElement("program",
                    new XAttribute("filename", safeFileName),
                    new XAttribute("label", partitionName),
                    new XAttribute("start_sector", partStartSector.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("num_partition_sectors", numSectorsToRead.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("SECTOR_SIZE_IN_BYTES", sectorSize.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("physical_partition_number", lun.ToString(CultureInfo.InvariantCulture))
                );

                rawprogramDoc.Root!.Add(programElement);
                successfulDumps++;
            }

            // Save rawprogram XML file
            var rawprogramXmlPath = Path.Combine(dumpSaveDir.FullName, $"rawprogram{lun}.xml");
            try
            {
                rawprogramDoc.Save(rawprogramXmlPath);
                Logging.Log($"Generated rawprogram XML file: '{rawprogramXmlPath}'", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Logging.Log($"Error saving rawprogram XML file: {ex.Message}", LogLevel.Error);
                return 1;
            }

            Logging.Log($"Dump completed: {successfulDumps}/{totalPartitions + (backupGptData != null && backupGptSectors > 0 ? 2 : 1)} files successfully dumped to '{dumpSaveDir.FullName}'", LogLevel.Info);
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
            Logging.Log($"IO Error: {ex.Message}", LogLevel.Error);
            return 1;
        }
        catch (Exception ex)
        {
            Logging.Log($"An unexpected error occurred in 'dump-rawprogram': {ex.Message}", LogLevel.Error);
            Logging.Log(ex.ToString(), LogLevel.Debug);
            return 1;
        }
        finally
        {
            commandStopwatch.Stop();
            Logging.Log($"'dump-rawprogram' command finished in {commandStopwatch.Elapsed.TotalSeconds:F2}s.", LogLevel.Debug);
        }

        return 0;
    }

    private static string CreateSafeFileName(string partitionName)
    {
        // Replace invalid characters with underscores
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeName = partitionName;

        foreach (var invalidChar in invalidChars)
        {
            safeName = safeName.Replace(invalidChar, '_');
        }

        // Ensure the filename is not empty and doesn't start with a dot
        if (string.IsNullOrWhiteSpace(safeName) || safeName.StartsWith('.'))
        {
            safeName = "partition_" + safeName;
        }

        // Add .bin extension if not present
        if (!safeName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
        {
            safeName += ".bin";
        }

        return safeName;
    }
}