using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;
using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;
using QCEDL.NET.PartitionTable;

namespace QCEDL.CLI.Commands;

internal sealed class DumpRawprogramCommand
{
    private static readonly Argument<DirectoryInfo> DumpSaveDirArgument = new("dump_save_dir", "The directory to save partition files and rawprogram XML.") { Arity = ArgumentArity.ExactlyOne };

    private static readonly Option<uint> LunOption = new(
        aliases: ["--lun", "-u"],
        description: "Specify the LUN number to dump partitions from.",
        getDefaultValue: () => 0);

    private static readonly Option<bool> GenXmlOnlyOption = new(
        aliases: ["--gen-xml-only"],
        description: "Only generate rawprogram and patch XML files, do not dump partitions.",
        getDefaultValue: () => false);

    private static readonly Option<string[]> SkipOption = new(
        aliases: ["--skip"],
        description: "Skip dumping specified partitions (comma-separated). These partitions will still be included in the rawprogram XML.",
        getDefaultValue: () => []);

    public static Command Create(GlobalOptionsBinder globalOptionsBinder)
    {
        var command = new Command("dump-rawprogram", "Reads all partitions to individual files from a certain LUN and generates rawprogram XML file.")
        {
            DumpSaveDirArgument,
            LunOption,
            GenXmlOnlyOption,
            SkipOption
        };

        command.SetHandler(ExecuteAsync,
            globalOptionsBinder,
            DumpSaveDirArgument,
            LunOption,
            GenXmlOnlyOption,
            SkipOption);

        return command;
    }

    private static async Task<int> ExecuteAsync(
        GlobalOptionsBinder globalOptions,
        DirectoryInfo dumpSaveDir,
        uint lun,
        bool genXmlOnly,
        string[] skipPartitions)
    {
        Logging.Log($"Executing 'dump-rawprogram' command: LUN {lun}, Save Directory '{dumpSaveDir.FullName}', GenXmlOnly={genXmlOnly}, SkipPartitions=[{string.Join(",", skipPartitions)}]...", LogLevel.Trace);
        var commandStopwatch = Stopwatch.StartNew();

        // Parse skip partitions into a set for efficient lookup
        var skipPartitionSet = new HashSet<string>(skipPartitions.SelectMany(p => p.Split(',')).Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p)), StringComparer.OrdinalIgnoreCase);
        if (skipPartitionSet.Count > 0)
        {
            Logging.Log($"Will skip dumping partitions: {string.Join(", ", skipPartitionSet)}", LogLevel.Info);
        }

        try
        {
            using var manager = new EdlManager(globalOptions);
            var isDirectMode = manager.IsHostDeviceMode || manager.IsRadxaWosMode;
            var effectiveLun = isDirectMode ? 0u : lun;
            var targetDescription = manager.IsHostDeviceMode
                ? "host device"
                : manager.IsRadxaWosMode
                    ? "Radxa WoS platform"
                    : $"LUN {effectiveLun}";

            var geometry = await manager.GetStorageGeometryAsync(effectiveLun);
            var sectorSize = geometry.SectorSize;
            var totalBlocks = geometry.TotalSectors ?? 0;

            Logging.Log($"Using sector size: {sectorSize} bytes for {targetDescription}.", LogLevel.Debug);
            if (totalBlocks == 0)
            {
                Logging.Log("Total block count unavailable; backup GPT calculations may be incomplete.", LogLevel.Warning);
            }

            dumpSaveDir.Create();

            // Read GPT to get partition information
            Logging.Log($"Reading GPT from {targetDescription}...");

            // Read enough sectors to contain GPT header and partition entries
            // Reading 64 sectors is usually safe for primary GPT header + entries
            const uint sectorsForGptRead = 64;
            byte[] gptData;
            try
            {
                gptData = await manager.ReadSectorsAsync(effectiveLun, 0, sectorsForGptRead);
            }
            catch (Exception readEx)
            {
                Logging.Log($"Failed to read GPT area from {targetDescription}. Error: {readEx.Message}", LogLevel.Error);
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
            var backupGptSectors = totalBlocks > 0 ? totalBlocks - gpt.Header.LastUsableLBA - 1 : 0; // From LastUsableLBA+1 to TotalBlocks-1

            Logging.Log($"GPT header indicates FirstUsableLBA: {gpt.Header.FirstUsableLBA}, LastUsableLBA: {gpt.Header.LastUsableLBA}", LogLevel.Debug);
            Logging.Log($"Calculated main GPT size: {mainGptSectors} sectors (0 to {gpt.Header.FirstUsableLBA - 1})", LogLevel.Debug);
            Logging.Log($"Calculated backup GPT size: {backupGptSectors} sectors ({gpt.Header.LastUsableLBA + 1} to {totalBlocks - 1})", LogLevel.Debug);

            // Save main GPT (from sector 0 to FirstUsableLBA-1)
            var mainGptFileName = $"gpt_main{lun}.bin";
            var mainGptFilePath = Path.Combine(dumpSaveDir.FullName, mainGptFileName);
            byte[]? mainGptData = null;
            if (mainGptSectors is > 0 and <= uint.MaxValue)
            {
                try
                {
                    mainGptData = await manager.ReadSectorsAsync(effectiveLun, 0, (uint)mainGptSectors);
                    if (mainGptData.Length >= sectorSize)
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
            }
            else
            {
                Logging.Log("Main GPT size exceeds supported range. Skipping main GPT dump.", LogLevel.Warning);
            }

            // Try to read backup GPT (from LastUsableLBA+1 to TotalBlocks-1)
            byte[]? backupGptData = null;
            var backupGptFileName = $"gpt_backup{lun}.bin";
            var backupGptFilePath = Path.Combine(dumpSaveDir.FullName, backupGptFileName);

            if (backupGptSectors > 0 && gpt.Header.LastUsableLBA + 1 < totalBlocks)
            {
                if (backupGptSectors <= uint.MaxValue)
                {
                    try
                    {
                        Logging.Log($"Reading backup GPT from LBA {gpt.Header.LastUsableLBA + 1} to {totalBlocks - 1}...", LogLevel.Debug);
                        backupGptData = await manager.ReadSectorsAsync(effectiveLun, gpt.Header.LastUsableLBA + 1, (uint)backupGptSectors);

                        if (backupGptData.Length >= sectorSize)
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
                    Logging.Log("Backup GPT size exceeds supported range. Skipping backup GPT dump.", LogLevel.Warning);
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
                new XAttribute("label", "PrimaryGPT"),
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
                    new XAttribute("label", "BackupGPT"),
                    new XAttribute("start_sector", $"NUM_DISK_SECTORS-{backupGptSectors}."),
                    new XAttribute("num_partition_sectors", backupGptSectors.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("SECTOR_SIZE_IN_BYTES", sectorSize.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("physical_partition_number", lun.ToString(CultureInfo.InvariantCulture))
                );
                rawprogramDoc.Root.Add(backupGptElement);
            }

            foreach (var partition in partitions)
            {
                var partitionName = partition.GetName().TrimEnd('\0');
                if (string.IsNullOrWhiteSpace(partitionName))
                {
                    continue;
                }

                var partStartSector = partition.FirstLBA;
                var partLastSector = partition.LastLBA;
                var numSectorsToRead = partLastSector - partStartSector + 1;

                if (partStartSector > uint.MaxValue || partLastSector > uint.MaxValue)
                {
                    continue;
                }

                var safeFileName = CreateSafeFileName(partitionName);

                var programElement = new XElement("program",
                    new XAttribute("filename", safeFileName),
                    new XAttribute("label", partitionName),
                    new XAttribute("start_sector", partStartSector.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("num_partition_sectors", numSectorsToRead.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("SECTOR_SIZE_IN_BYTES", sectorSize.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("physical_partition_number", lun.ToString(CultureInfo.InvariantCulture))
                );
                rawprogramDoc.Root!.Add(programElement);
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

            // Generate patch XML file for GPT header fixes
            var patchXmlPath = Path.Combine(dumpSaveDir.FullName, $"patch{lun}.xml");
            try
            {
                var patchDoc = GeneratePatchXml(lun, sectorSize, mainGptFileName, backupGptFileName);
                patchDoc.Save(patchXmlPath);
                Logging.Log($"Generated patch XML file: '{patchXmlPath}'", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Logging.Log($"Error saving patch XML file: {ex.Message}", LogLevel.Error);
                return 1;
            }

            if (genXmlOnly)
            {
                Logging.Log("--gen-xml-only specified, skipping partition dump.", LogLevel.Info);
                return 0;
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

                // Check if this partition should be skipped
                if (skipPartitionSet.Contains(partitionName))
                {
                    Logging.Log($"Skipping partition '{partitionName}' as requested by --skip parameter.", LogLevel.Info);
                    continue;
                }

                var partStartSector = partition.FirstLBA;
                var partLastSector = partition.LastLBA;
                var numSectorsToRead = partLastSector - partStartSector + 1;

                if (numSectorsToRead == 0)
                {
                    Logging.Log($"Warning: Partition '{partitionName}' has zero size. Skipping.", LogLevel.Warning);
                    continue;
                }

                var totalBytesDecimal = (decimal)numSectorsToRead * sectorSize;
                var totalBytes = totalBytesDecimal > long.MaxValue ? long.MaxValue : (long)totalBytesDecimal;

                var safeFileName = CreateSafeFileName(partitionName);
                var partitionFilePath = Path.Combine(dumpSaveDir.FullName, safeFileName);

                Logging.Log($"Dumping partition '{partitionName}' ({numSectorsToRead} sectors, {totalBytesDecimal / (1024.0m * 1024.0m):F2} MiB) to '{partitionFilePath}'...", LogLevel.Debug);

                long bytesReadReported = 0;
                var readStopwatch = new Stopwatch();

                void ProgressAction(long current, long total)
                {
                    bytesReadReported = current;
                    var percentage = total == 0 ? 100 : current * 100.0 / total;
                    var elapsed = readStopwatch.Elapsed;
                    var speed = elapsed.TotalSeconds > 0 ? current / elapsed.TotalSeconds : 0;
                    var speedStr = "N/A";
                    if (elapsed.TotalSeconds > 0.1)
                    {
                        speedStr = speed > 1024 * 1024 ? $"{speed / (1024 * 1024):F2} MiB/s" :
                            speed > 1024 ? $"{speed / 1024:F2} KiB/s" :
                            $"{speed:F0} B/s";
                    }
                    Console.Write($"\rDumping {partitionName}: {percentage:F1}% ({current / (1024.0 * 1024.0):F2} / {total / (1024.0 * 1024.0):F2} MiB) [{speedStr}]      ");
                }

                try
                {
                    using var fileStream = File.Open(partitionFilePath, FileMode.Create, FileAccess.Write, FileShare.None);

                    readStopwatch.Start();
                    await manager.ReadSectorsToStreamAsync(lun, partStartSector, numSectorsToRead, fileStream, ProgressAction);
                    readStopwatch.Stop();
                }
                catch (Exception ex)
                {
                    Logging.Log($"Error dumping partition '{partitionName}': {ex.Message}", LogLevel.Error);
                    Logging.Log(ex.ToString(), LogLevel.Debug);
                    Console.WriteLine();
                    TryDeleteFile(partitionFilePath);
                    continue;
                }

                Console.WriteLine();

                if (bytesReadReported == 0 && totalBytes > 0)
                {
                    bytesReadReported = totalBytes;
                }

                Logging.Log($"Successfully dumped partition '{partitionName}' ({bytesReadReported / (1024.0 * 1024.0):F2} MiB) in {readStopwatch.Elapsed.TotalSeconds:F2}s.", LogLevel.Debug);
                successfulDumps++;
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

    private static XDocument GeneratePatchXml(uint lun, uint sectorSize, string mainGptFileName, string backupGptFileName)
    {
        var patchesElement = new XElement("patches");
        var doc = new XDocument(patchesElement);

        // Patch 1: Update Primary Header with LastUsableLBA
        AddPatch(patchesElement, "1", 48, lun, 8, "NUM_DISK_SECTORS-6.", mainGptFileName, sectorSize, "Update Primary Header with LastUseableLBA.");
        AddPatch(patchesElement, "1", 48, lun, 8, "NUM_DISK_SECTORS-6.", "DISK", sectorSize, "Update Primary Header with LastUseableLBA.");

        // Patch 2: Update Backup Header with LastUsableLBA
        AddPatch(patchesElement, "4", 48, lun, 8, "NUM_DISK_SECTORS-6.", backupGptFileName, sectorSize, "Update Backup Header with LastUseableLBA.");
        AddPatch(patchesElement, "NUM_DISK_SECTORS-1.", 48, lun, 8, "NUM_DISK_SECTORS-6.", "DISK", sectorSize, "Update Backup Header with LastUseableLBA.");

        // Patch 3: Update Primary Header with BackupGPT Header Location
        AddPatch(patchesElement, "1", 32, lun, 8, "NUM_DISK_SECTORS-1.", mainGptFileName, sectorSize, "Update Primary Header with BackupGPT Header Location.");
        AddPatch(patchesElement, "1", 32, lun, 8, "NUM_DISK_SECTORS-1.", "DISK", sectorSize, "Update Primary Header with BackupGPT Header Location.");

        // Patch 4: Update Backup Header with CurrentLBA
        AddPatch(patchesElement, "4", 24, lun, 8, "NUM_DISK_SECTORS-1.", backupGptFileName, sectorSize, "Update Backup Header with CurrentLBA.");
        AddPatch(patchesElement, "NUM_DISK_SECTORS-1.", 24, lun, 8, "NUM_DISK_SECTORS-1.", "DISK", sectorSize, "Update Backup Header with CurrentLBA.");

        // Patch 5: Update Backup Header with Partition Array Location
        AddPatch(patchesElement, "4", 72, lun, 8, "NUM_DISK_SECTORS-5.", backupGptFileName, sectorSize, "Update Backup Header with Partition Array Location.");
        AddPatch(patchesElement, "NUM_DISK_SECTORS-1", 72, lun, 8, "NUM_DISK_SECTORS-5.", "DISK", sectorSize, "Update Backup Header with Partition Array Location.");

        // Patch 6: Update Primary Header with CRC of Partition Array
        AddPatch(patchesElement, "1", 88, lun, 4, "CRC32(2,4096)", mainGptFileName, sectorSize, "Update Primary Header with CRC of Partition Array.");
        AddPatch(patchesElement, "1", 88, lun, 4, "CRC32(2,4096)", "DISK", sectorSize, "Update Primary Header with CRC of Partition Array.");

        // Patch 7: Update Backup Header with CRC of Partition Array
        AddPatch(patchesElement, "4", 88, lun, 4, "CRC32(0,4096)", backupGptFileName, sectorSize, "Update Backup Header with CRC of Partition Array.");
        AddPatch(patchesElement, "NUM_DISK_SECTORS-1.", 88, lun, 4, "CRC32(NUM_DISK_SECTORS-5.,4096)", "DISK", sectorSize, "Update Backup Header with CRC of Partition Array.");

        // Patch 8: Zero Out Header CRC in Primary Header
        AddPatch(patchesElement, "1", 16, lun, 4, "0", mainGptFileName, sectorSize, "Zero Out Header CRC in Primary Header.");
        AddPatch(patchesElement, "1", 16, lun, 4, "CRC32(1,92)", mainGptFileName, sectorSize, "Update Primary Header with CRC of Primary Header.");
        AddPatch(patchesElement, "1", 16, lun, 4, "0", "DISK", sectorSize, "Zero Out Header CRC in Primary Header.");
        AddPatch(patchesElement, "1", 16, lun, 4, "CRC32(1,92)", "DISK", sectorSize, "Update Primary Header with CRC of Primary Header.");

        // Patch 9: Zero Out Header CRC in Backup Header
        AddPatch(patchesElement, "4", 16, lun, 4, "0", backupGptFileName, sectorSize, "Zero Out Header CRC in Backup Header.");
        AddPatch(patchesElement, "4", 16, lun, 4, "CRC32(4,92)", backupGptFileName, sectorSize, "Update Backup Header with CRC of Backup Header.");
        AddPatch(patchesElement, "NUM_DISK_SECTORS-1.", 16, lun, 4, "0", "DISK", sectorSize, "Zero Out Header CRC in Backup Header.");
        AddPatch(patchesElement, "NUM_DISK_SECTORS-1.", 16, lun, 4, "CRC32(NUM_DISK_SECTORS-1.,92)", "DISK", sectorSize, "Update Backup Header with CRC of Backup Header.");

        return doc;
    }

    private static void AddPatch(XElement patchesElement, string startSector, uint byteOffset, uint physicalPartitionNumber, uint sizeInBytes, string value, string filename, uint sectorSizeInBytes, string what)
    {
        var patchElement = new XElement("patch",
            new XAttribute("start_sector", startSector),
            new XAttribute("byte_offset", byteOffset.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("physical_partition_number", physicalPartitionNumber.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("size_in_bytes", sizeInBytes.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("value", value),
            new XAttribute("filename", filename),
            new XAttribute("SECTOR_SIZE_IN_BYTES", sectorSizeInBytes.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("what", what)
        );
        patchesElement.Add(patchElement);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            Logging.Log($"Could not delete file '{path}': {ex.Message}", LogLevel.Warning);
        }
    }
}