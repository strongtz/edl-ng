using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;
using System.CommandLine;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace QCEDL.CLI.Commands
{
    internal class RawProgramCommand
    {
        private static readonly Argument<string[]> XmlFilePatternsArgument =
            new("xmlfile_patterns", "Paths or patterns for rawprogram and patch XML files (e.g., rawprogram0.xml patch0.xml rawprogram*.xml patch*.xml).")
            { Arity = ArgumentArity.OneOrMore };

        public static Command Create(GlobalOptionsBinder globalOptionsBinder)
        {
            var command = new Command("rawprogram", "Processes rawprogramN.xml and patchN.xml files for flashing.")
            {
                XmlFilePatternsArgument // Use the new argument
            };

            command.SetHandler(ExecuteAsync, globalOptionsBinder, XmlFilePatternsArgument);
            return command;
        }

        private static async Task<int> ExecuteAsync(GlobalOptionsBinder globalOptions, string[] xmlFilePatterns)
        {
            Logging.Log("Executing 'rawprogram' command...", LogLevel.Trace);

            List<FileInfo> resolvedXmlFiles = [];
            string currentDirectory = Environment.CurrentDirectory;

            foreach (string pattern in xmlFilePatterns)
            {
                string dirName = Path.GetDirectoryName(pattern);
                string fileNamePattern = Path.GetFileName(pattern);

                string searchDir = string.IsNullOrEmpty(dirName) ? currentDirectory : Path.GetFullPath(Path.Combine(currentDirectory, dirName));

                if (!Directory.Exists(searchDir))
                {
                    Logging.Log($"Error: Directory '{searchDir}' for pattern '{pattern}' not found.", LogLevel.Error);
                    return 1;
                }

                try
                {
                    var foundFiles = Directory.EnumerateFiles(searchDir, fileNamePattern, SearchOption.TopDirectoryOnly);
                    if (!foundFiles.Any())
                    {
                        // If no files found by globbing, check if the pattern itself is a literal file path that exists
                        FileInfo literalFile = new FileInfo(Path.Combine(searchDir, fileNamePattern));
                        if (literalFile.Exists)
                        {
                            resolvedXmlFiles.Add(literalFile);
                            Logging.Log($"Found literal file: {literalFile.FullName}", LogLevel.Trace);
                        }
                        else
                        {
                            Logging.Log($"Warning: No files found matching pattern '{pattern}' in directory '{searchDir}'.", LogLevel.Warning);
                        }
                    }
                    else
                    {
                        foreach (var file in foundFiles)
                        {
                            resolvedXmlFiles.Add(new FileInfo(file));
                            Logging.Log($"Found file from pattern '{pattern}': {file}", LogLevel.Trace);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log($"Error enumerating files for pattern '{pattern}' in directory '{searchDir}': {ex.Message}", LogLevel.Error);
                    return 1;
                }
            }

            if (!resolvedXmlFiles.Any())
            {
                Logging.Log("Error: No XML files found after resolving patterns.", LogLevel.Error);
                return 1;
            }

            // Deduplicate in case patterns overlap or literal files are also matched by patterns
            resolvedXmlFiles = resolvedXmlFiles.DistinctBy(f => f.FullName).ToList();
            Logging.Log($"Total unique XML files to process: {resolvedXmlFiles.Count}", LogLevel.Debug);

            // Validate all files exist
            foreach (var file in resolvedXmlFiles)
            {
                if (!file.Exists)
                {
                    Logging.Log($"Error: XML file '{file.FullName}' not found.", LogLevel.Error);
                    return 1;
                }
            }

            var rawProgramFilesMap = new Dictionary<int, FileInfo>();
            var patchFilesMap = new Dictionary<int, FileInfo>();
            var rawProgramRegex = new Regex(@"rawprogram(\d+)\.xml$", RegexOptions.IgnoreCase);
            var patchRegex = new Regex(@"patch(\d+)\.xml$", RegexOptions.IgnoreCase);

            foreach (var file in resolvedXmlFiles)
            {
                var rawMatch = rawProgramRegex.Match(file.Name);
                if (rawMatch.Success && int.TryParse(rawMatch.Groups[1].Value, out int lun))
                {
                    if (rawProgramFilesMap.ContainsKey(lun))
                    {
                        Logging.Log($"Warning: Duplicate rawprogram file for LUN {lun}: {file.Name} and {rawProgramFilesMap[lun].Name}. Using first one found.", LogLevel.Warning);
                    }
                    else
                    {
                        rawProgramFilesMap[lun] = file;
                    }
                }
                else
                {
                    var patchMatch = patchRegex.Match(file.Name);
                    if (patchMatch.Success && int.TryParse(patchMatch.Groups[1].Value, out int lunPatch))
                    {
                        if (patchFilesMap.ContainsKey(lunPatch))
                        {
                            Logging.Log($"Warning: Duplicate patch file for LUN {lunPatch}: {file.Name} and {patchFilesMap[lunPatch].Name}. Using first one found.", LogLevel.Warning);
                        }
                        else
                        {
                            patchFilesMap[lunPatch] = file;
                        }
                    }
                    else
                    {
                        Logging.Log($"Warning: Skipping file with unrecognized name format: {file.Name}. Expected rawprogramN.xml or patchN.xml.", LogLevel.Warning);
                    }
                }
            }

            var sortedLunsToProcess = rawProgramFilesMap.Keys.OrderBy(k => k).ToList();

            if (!sortedLunsToProcess.Any())
            {
                Logging.Log("Error: No rawprogramN.xml files found to process.", LogLevel.Error);
                return 1;
            }

            StorageType storageType = globalOptions.MemoryType ?? StorageType.UFS;
            var lunTotalSectorsCache = new Dictionary<uint, ulong>();

            try
            {
                using var manager = new EdlManager(globalOptions);
                await manager.EnsureFirehoseModeAsync();
                await manager.ConfigureFirehoseAsync(); // Initial configure

                foreach (int lunKey in sortedLunsToProcess)
                {
                    var rawFile = rawProgramFilesMap[lunKey];
                    Logging.Log($"--- Processing LUN {lunKey} using {rawFile.Name} ---", LogLevel.Info);

                    XDocument rawDoc;
                    try
                    {
                        rawDoc = XDocument.Load(rawFile.FullName);
                    }
                    catch (Exception ex)
                    {
                        Logging.Log($"Error parsing XML file '{rawFile.FullName}': {ex.Message}", LogLevel.Error);
                        return 1; // Abort on XML error
                    }

                    if (rawDoc.Root == null || rawDoc.Root.Name != "data")
                    {
                        Logging.Log($"Invalid XML structure in '{rawFile.FullName}': Root element must be <data>.", LogLevel.Error);
                        return 1;
                    }

                    var programElements = rawDoc.Root.Elements("program").ToList();
                    Logging.Log($"Found {programElements.Count} <program> elements in {rawFile.Name}.", LogLevel.Debug);

                    foreach (var progElement in programElements)
                    {
                        string? filename = progElement.Attribute("filename")?.Value;
                        if (string.IsNullOrEmpty(filename))
                        {
                            Logging.Log($"Skipping <program> element with empty filename (Label: {progElement.Attribute("label")?.Value ?? "N/A"}).", LogLevel.Debug);
                            continue;
                        }

                        string? startSectorStr = progElement.Attribute("start_sector")?.Value;
                        string? sectorSizeStr = progElement.Attribute("SECTOR_SIZE_IN_BYTES")?.Value;
                        string? physicalPartitionNumberStr = progElement.Attribute("physical_partition_number")?.Value;

                        if (string.IsNullOrEmpty(startSectorStr) || string.IsNullOrEmpty(sectorSizeStr) || string.IsNullOrEmpty(physicalPartitionNumberStr))
                        {
                            Logging.Log($"Error: <program> element (Label: {progElement.Attribute("label")?.Value}) in {rawFile.Name} is missing required attributes (start_sector, SECTOR_SIZE_IN_BYTES, physical_partition_number).", LogLevel.Error);
                            return 1;
                        }

                        if (!uint.TryParse(sectorSizeStr, out uint sectorSize) || sectorSize == 0)
                        {
                            Logging.Log($"Error: Invalid SECTOR_SIZE_IN_BYTES '{sectorSizeStr}' for <program> (Label: {progElement.Attribute("label")?.Value}).", LogLevel.Error);
                            return 1;
                        }
                        if (!uint.TryParse(physicalPartitionNumberStr, out uint targetLun))
                        {
                            Logging.Log($"Error: Invalid physical_partition_number '{physicalPartitionNumberStr}' for <program> (Label: {progElement.Attribute("label")?.Value}).", LogLevel.Error);
                            return 1;
                        }

                        ulong numDiskSectorsForTargetLun = 0;
                        if (startSectorStr.Contains("NUM_DISK_SECTORS"))
                        {
                            if (lunTotalSectorsCache.TryGetValue(targetLun, out ulong cachedSectors))
                            {
                                numDiskSectorsForTargetLun = cachedSectors;
                            }
                            else
                            {
                                Logging.Log($"Fetching NUM_DISK_SECTORS for LUN {targetLun}...", LogLevel.Debug);
                                Qualcomm.EmergencyDownload.Layers.APSS.Firehose.JSON.StorageInfo.Root? storageInfo = null;
                                try
                                {
                                    storageInfo = await Task.Run(() => manager.Firehose.GetStorageInfo(storageType, targetLun));
                                }
                                catch (Exception ex)
                                {
                                    Logging.Log($"Could not get storage info for LUN {targetLun} to resolve NUM_DISK_SECTORS. Error: {ex.Message}", LogLevel.Error);
                                    return 1; // Cannot proceed if NUM_DISK_SECTORS is needed but unavailable
                                }
                                if (storageInfo?.storage_info?.total_blocks <= 0)
                                {
                                    Logging.Log($"Error: NUM_DISK_SECTORS (total_blocks) for LUN {targetLun} is invalid or zero.", LogLevel.Error);
                                    return 1;
                                }
                                numDiskSectorsForTargetLun = (ulong)storageInfo.storage_info.total_blocks;
                                lunTotalSectorsCache[targetLun] = numDiskSectorsForTargetLun;
                                Logging.Log($"NUM_DISK_SECTORS for LUN {targetLun} is {numDiskSectorsForTargetLun}.", LogLevel.Debug);
                            }
                        }

                        if (!TryParseSectorExpression(startSectorStr, numDiskSectorsForTargetLun, out ulong resolvedStartSector))
                        {
                            Logging.Log($"Error: Could not parse start_sector expression '{startSectorStr}' for <program> (Label: {progElement.Attribute("label")?.Value}).", LogLevel.Error);
                            return 1;
                        }

                        FileInfo imageFile = new FileInfo(Path.Combine(rawFile.DirectoryName ?? "", filename));
                        if (!imageFile.Exists)
                        {
                            Logging.Log($"Error: Image file '{imageFile.FullName}' for <program> (Label: {progElement.Attribute("label")?.Value}) not found. Skipping this file.", LogLevel.Error);
                            continue;
                        }

                        Logging.Log($"Programming '{filename}' to LUN {targetLun}, StartSector {resolvedStartSector}, SectorSize {sectorSize}.", LogLevel.Info);

                        byte[] originalData = await File.ReadAllBytesAsync(imageFile.FullName);
                        if (originalData.Length == 0)
                        {
                            Logging.Log($"Warning: Image file '{imageFile.FullName}' is empty. Skipping write for this file.", LogLevel.Warning);
                            continue;
                        }

                        byte[] dataToWrite;
                        long originalLength = originalData.Length;
                        long remainder = originalLength % sectorSize;
                        if (remainder != 0)
                        {
                            long paddedLength = originalLength + (sectorSize - remainder);
                            Logging.Log($"Padding '{filename}' from {originalLength} to {paddedLength} bytes (SectorSize: {sectorSize}).", LogLevel.Debug);
                            dataToWrite = new byte[paddedLength];
                            Buffer.BlockCopy(originalData, 0, dataToWrite, 0, (int)originalLength);
                        }
                        else
                        {
                            dataToWrite = originalData;
                        }

                        // The num_partition_sectors from XML is for the <program> tag sent to device.
                        // The C# Firehose.Program method calculates its own based on dataToWrite.
                        // We'll use the one from XML for the XML attribute if needed, but dataToWrite drives the actual write.
                        // string numPartitionSectorsFromXml = progElement.Attribute("num_partition_sectors")?.Value ?? "0";


                        bool success = await Task.Run(() => manager.Firehose.Program(
                            storageType,
                            targetLun,
                            sectorSize,
                            (uint)resolvedStartSector,
                            filename, // filename for XML attribute in Firehose command
                            dataToWrite
                        ));

                        if (!success)
                        {
                            Logging.Log($"Failed to program '{filename}'. Aborting.", LogLevel.Error);
                            return 1;
                        }
                        Logging.Log($"Successfully programmed '{filename}'.", LogLevel.Info);
                    } // End foreach progElement

                    // Process corresponding patch file, if it exists
                    if (patchFilesMap.TryGetValue(lunKey, out var patchFile))
                    {
                        Logging.Log($"--- Patching LUN {lunKey} using {patchFile.Name} ---", LogLevel.Info);
                        XDocument patchDoc;
                        try
                        {
                            patchDoc = XDocument.Load(patchFile.FullName);
                        }
                        catch (Exception ex)
                        {
                            Logging.Log($"Error parsing XML file '{patchFile.FullName}': {ex.Message}", LogLevel.Error);
                            return 1; // Abort on XML error
                        }

                        if (patchDoc.Root == null || patchDoc.Root.Name != "patches")
                        {
                            Logging.Log($"Invalid XML structure in '{patchFile.FullName}': Root element must be <patches>.", LogLevel.Error);
                            return 1;
                        }

                        var patchElements = patchDoc.Root.Elements("patch").ToList();
                        Logging.Log($"Found {patchElements.Count} <patch> elements in {patchFile.Name}.", LogLevel.Debug);
                        int patchIndex = 0;
                        foreach (var patchElement in patchElements)
                        {
                            patchIndex++;
                            string patchElementString = patchElement.ToString(SaveOptions.DisableFormatting);
                            string fullXmlPayload = $"<?xml version=\"1.0\" ?><data>{patchElementString}</data>";

                            Logging.Log($"Sending patch command {patchIndex}/{patchElements.Count} from {patchFile.Name}", LogLevel.Debug);
                            Logging.Log($"Patch XML: {patchElementString}", LogLevel.Trace);


                            bool patchSuccess = await Task.Run(() => manager.Firehose.SendRawXmlAndGetResponse(fullXmlPayload));
                            if (patchSuccess)
                            {
                                Logging.Log($"Patch command {patchIndex} ACKed.", LogLevel.Debug);
                            }
                            else
                            {
                                Logging.Log($"Failed to send patch command {patchIndex} or received NAK.", LogLevel.Error);
                                Logging.Log($"Aborting 'rawprogram'. {patchIndex - 1}/{patchElements.Count} patches succeeded before failure.", LogLevel.Error);
                                return 1;
                            }
                        }
                        Logging.Log($"Patching for LUN {lunKey} using {patchFile.Name} completed.\n", LogLevel.Info);
                    }
                    else
                    {
                        Logging.Log($"Note: patch{lunKey}.xml not found. Skipping patching for LUN {lunKey}.", LogLevel.Info);
                    }
                    Logging.Log($"--- Finished processing LUN {lunKey} ---\n", LogLevel.Debug);
                }
            }
            catch (FileNotFoundException ex)
            {
                Logging.Log(ex.Message, LogLevel.Error);
                return 1;
            }
            catch (ArgumentException ex)
            {
                Logging.Log($"Argument Error: {ex.Message}", LogLevel.Error);
                return 1;
            }
            catch (InvalidOperationException ex)
            {
                Logging.Log($"Operation Error: {ex.Message}", LogLevel.Error);
                return 1;
            }
            catch (IOException ex)
            {
                Logging.Log($"IO Error: {ex.Message}", LogLevel.Error);
                return 1;
            }
            catch (Exception ex)
            {
                Logging.Log($"An unexpected error occurred in 'rawprogram': {ex.Message}", LogLevel.Error);
                Logging.Log(ex.ToString(), LogLevel.Debug);
                return 1;
            }

            Logging.Log("'rawprogram' command finished successfully.", LogLevel.Info);
            return 0;
        }

        private static bool TryParseSectorExpression(string expression, ulong totalDiskSectorsForLun, out ulong resultSector)
        {
            resultSector = 0;
            if (string.IsNullOrWhiteSpace(expression)) return false;

            string trimmedExpression = expression.Trim();

            // Try direct ulong parse first
            if (ulong.TryParse(trimmedExpression, NumberStyles.Integer, CultureInfo.InvariantCulture, out resultSector))
            {
                return true;
            }
            // Try parsing after removing a potential trailing dot (from XML examples)
            if (trimmedExpression.EndsWith(".") && ulong.TryParse(trimmedExpression.Substring(0, trimmedExpression.Length - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out resultSector))
            {
                return true;
            }

            // Handle NUM_DISK_SECTORS expressions
            if (trimmedExpression.Contains("NUM_DISK_SECTORS"))
            {
                if (totalDiskSectorsForLun == 0) // Not fetched or invalid
                {
                    Logging.Log("Cannot resolve NUM_DISK_SECTORS because totalDiskSectorsForLun is 0.", LogLevel.Error);
                    return false;
                }

                // Regex for "NUM_DISK_SECTORS" optionally followed by "- X" or "+ X"
                // Example: "NUM_DISK_SECTORS - 5" or "NUM_DISK_SECTORS-5."
                var match = Regex.Match(trimmedExpression, @"^\s*NUM_DISK_SECTORS\s*(?:([+-])\s*(\d+))?\s*\.?\s*$", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    resultSector = totalDiskSectorsForLun;
                    if (match.Groups[1].Success && match.Groups[2].Success) // Operator and operand exist
                    {
                        string op = match.Groups[1].Value;
                        if (ulong.TryParse(match.Groups[2].Value, out ulong operand))
                        {
                            if (op == "-")
                            {
                                if (resultSector < operand) { Logging.Log($"Error: NUM_DISK_SECTORS ({resultSector}) - {operand} results in negative value.", LogLevel.Error); return false; }
                                resultSector -= operand;
                            }
                            else // op == "+"
                            {
                                resultSector += operand;
                            }
                            return true;
                        }
                        else { Logging.Log($"Failed to parse operand '{match.Groups[2].Value}' in expression '{trimmedExpression}'.", LogLevel.Error); return false; }
                    }
                    else // Just "NUM_DISK_SECTORS"
                    {
                        return true;
                    }
                }
                else
                {
                    Logging.Log($"Unsupported NUM_DISK_SECTORS expression format: '{trimmedExpression}'.", LogLevel.Error);
                    return false;
                }
            }
            return false;
        }
    }
}
