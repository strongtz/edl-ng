using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.JSON.StorageInfo;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace QCEDL.CLI.Commands;

internal sealed class RawProgramCommand
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
        var commandStopwatch = Stopwatch.StartNew();

        List<FileInfo> resolvedXmlFiles = [];
        var currentDirectory = Environment.CurrentDirectory;

        foreach (var pattern in xmlFilePatterns)
        {
            var dirName = Path.GetDirectoryName(pattern);
            var fileNamePattern = Path.GetFileName(pattern);

            var searchDir = string.IsNullOrEmpty(dirName) ? currentDirectory : Path.GetFullPath(Path.Combine(currentDirectory, dirName));

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
                    var literalFile = new FileInfo(Path.Combine(searchDir, fileNamePattern));
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
                        resolvedXmlFiles.Add(new(file));
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

        if (resolvedXmlFiles.Count == 0)
        {
            Logging.Log("Error: No XML files found after resolving patterns.", LogLevel.Error);
            return 1;
        }

        // Deduplicate in case patterns overlap or literal files are also matched by patterns
        resolvedXmlFiles = [.. resolvedXmlFiles.DistinctBy(f => f.FullName)];
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
            if (rawMatch.Success && int.TryParse(rawMatch.Groups[1].Value, out var lun))
            {
                if (rawProgramFilesMap.TryGetValue(lun, out var value))
                {
                    Logging.Log($"Warning: Duplicate rawprogram file for LUN {lun}: {file.Name} and {value.Name}. Using first one found.", LogLevel.Warning);
                }
                else
                {
                    rawProgramFilesMap[lun] = file;
                }
            }
            else
            {
                var patchMatch = patchRegex.Match(file.Name);
                if (patchMatch.Success && int.TryParse(patchMatch.Groups[1].Value, out var lunPatch))
                {
                    if (patchFilesMap.TryGetValue(lunPatch, out var value))
                    {
                        Logging.Log($"Warning: Duplicate patch file for LUN {lunPatch}: {file.Name} and {value.Name}. Using first one found.", LogLevel.Warning);
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

        if (sortedLunsToProcess.Count == 0)
        {
            Logging.Log("Error: No rawprogramN.xml files found to process.", LogLevel.Error);
            return 1;
        }

        var storageType = globalOptions.MemoryType ?? StorageType.Ufs;
        var lunTotalSectorsCache = new Dictionary<uint, ulong>();

        try
        {
            using var manager = new EdlManager(globalOptions);
            await manager.EnsureFirehoseModeAsync();
            await manager.ConfigureFirehoseAsync(); // Initial configure

            foreach (var lunKey in sortedLunsToProcess)
            {
                var rawFile = rawProgramFilesMap[lunKey];
                Logging.Log($"--- Processing LUN {lunKey} using {rawFile.Name} ---");

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

                var maxFilenameDisplayLength = 0;
                if (programElements.Count != 0)
                {
                    foreach (var progElementForLengthCalc in programElements)
                    {
                        var filenameCalc = progElementForLengthCalc.Attribute("filename")?.Value;
                        if (string.IsNullOrEmpty(filenameCalc))
                        {
                            continue;
                        }
                        var labelCalc = progElementForLengthCalc.Attribute("label")?.Value ?? "N/A";
                        // We want to align based on the "Writing label (filename): " part
                        var currentDisplayString = $"Writing {labelCalc} ({filenameCalc}): ";
                        if (currentDisplayString.Length > maxFilenameDisplayLength)
                        {
                            maxFilenameDisplayLength = currentDisplayString.Length;
                        }
                    }
                }
                // Add a small buffer to ensure some space after the longest prefix, e.g., 2 spaces
                maxFilenameDisplayLength += 2;

                var programElementIndex = 0;

                foreach (var progElement in programElements)
                {
                    programElementIndex++;
                    var filename = progElement.Attribute("filename")?.Value;
                    var label = progElement.Attribute("label")?.Value ?? "N/A";

                    if (string.IsNullOrEmpty(filename))
                    {
                        Logging.Log($"Skipping <program> element with empty filename (Label: {progElement.Attribute("label")?.Value ?? "N/A"}).", LogLevel.Debug);
                        continue;
                    }

                    var startSectorStr = progElement.Attribute("start_sector")?.Value;
                    var sectorSizeStr = progElement.Attribute("SECTOR_SIZE_IN_BYTES")?.Value;
                    var physicalPartitionNumberStr = progElement.Attribute("physical_partition_number")?.Value;

                    if (string.IsNullOrEmpty(startSectorStr) || string.IsNullOrEmpty(sectorSizeStr) || string.IsNullOrEmpty(physicalPartitionNumberStr))
                    {
                        Logging.Log($"Error: <program> element (Label: {progElement.Attribute("label")?.Value}) in {rawFile.Name} is missing required attributes (start_sector, SECTOR_SIZE_IN_BYTES, physical_partition_number).", LogLevel.Error);
                        continue;
                    }

                    if (!uint.TryParse(sectorSizeStr, out var sectorSize) || sectorSize == 0)
                    {
                        Logging.Log($"Error: Invalid SECTOR_SIZE_IN_BYTES '{sectorSizeStr}' for <program> (Label: {progElement.Attribute("label")?.Value}).", LogLevel.Error);
                        continue;
                    }
                    if (!uint.TryParse(physicalPartitionNumberStr, out var targetLun))
                    {
                        Logging.Log($"Error: Invalid physical_partition_number '{physicalPartitionNumberStr}' for <program> (Label: {progElement.Attribute("label")?.Value}).", LogLevel.Error);
                        continue;
                    }

                    ulong numDiskSectorsForTargetLun = 0;
                    if (startSectorStr.Contains("NUM_DISK_SECTORS"))
                    {
                        if (lunTotalSectorsCache.TryGetValue(targetLun, out var cachedSectors))
                        {
                            numDiskSectorsForTargetLun = cachedSectors;
                        }
                        else
                        {
                            Logging.Log($"Fetching NUM_DISK_SECTORS for LUN {targetLun}...", LogLevel.Debug);
                            Root? storageInfo = null;
                            try
                            {
                                storageInfo = await Task.Run(() => manager.Firehose.GetStorageInfo(storageType, targetLun, globalOptions.Slot));
                            }
                            catch (Exception ex)
                            {
                                Logging.Log($"Could not get storage info for LUN {targetLun} to resolve NUM_DISK_SECTORS. Error: {ex.Message}", LogLevel.Error);
                                return 1; // Cannot proceed if NUM_DISK_SECTORS is needed but unavailable
                            }
                            if (storageInfo?.StorageInfo?.TotalBlocks <= 0)
                            {
                                Logging.Log($"Error: NUM_DISK_SECTORS (total_blocks) for LUN {targetLun} is invalid or zero.", LogLevel.Error);
                                return 1;
                            }
                            numDiskSectorsForTargetLun = (ulong)(storageInfo?.StorageInfo?.TotalBlocks ?? 0);
                            lunTotalSectorsCache[targetLun] = numDiskSectorsForTargetLun;
                            Logging.Log($"NUM_DISK_SECTORS for LUN {targetLun} is {numDiskSectorsForTargetLun}.", LogLevel.Debug);
                        }
                    }

                    if (!TryParseSectorExpression(startSectorStr, numDiskSectorsForTargetLun, out var resolvedStartSector))
                    {
                        Logging.Log($"Error: Could not parse start_sector expression '{startSectorStr}' for <program> (Label: {progElement.Attribute("label")?.Value}).", LogLevel.Error);
                        return 1;
                    }

                    var imageFile = new FileInfo(Path.Combine(rawFile.DirectoryName ?? "", filename));
                    if (!imageFile.Exists)
                    {
                        Logging.Log($"Error: Image file '{imageFile.FullName}' for <program> (Label: {progElement.Attribute("label")?.Value}) not found. Skipping this file.", LogLevel.Error);
                        continue;
                    }

                    if (imageFile.Length == 0 && !string.Equals(filename, "ZERO", StringComparison.OrdinalIgnoreCase))
                    {
                        Logging.Log($"Warning: Image file '{imageFile.FullName}' (Label: {label}) is empty. Skipping write for this file.", LogLevel.Warning);
                        continue;
                    }

                    var originalFileLength = imageFile.Length;
                    long totalBytesToWriteIncludingPadding;
                    uint numSectorsForXmlAttribute; // This is what goes into the <program num_partition_sectors="..."> attribute

                    var remainder = originalFileLength % sectorSize;
                    if (remainder != 0)
                    {
                        totalBytesToWriteIncludingPadding = originalFileLength + (sectorSize - remainder);
                        Logging.Log($"Padding '{filename}' (Label: {label}) from {originalFileLength} to {totalBytesToWriteIncludingPadding} bytes (SectorSize: {sectorSize}).", LogLevel.Debug);
                    }
                    else
                    {
                        totalBytesToWriteIncludingPadding = originalFileLength;
                    }
                    numSectorsForXmlAttribute = (uint)(totalBytesToWriteIncludingPadding / sectorSize);

                    Logging.Log($"Programming '{filename}' (Label: {label}) to LUN {targetLun}, StartSector {resolvedStartSector}, SectorSize {sectorSize}. Total to stream: {totalBytesToWriteIncludingPadding} bytes.", LogLevel.Debug);

                    long bytesWrittenReported = 0;
                    var writeStopwatch = new Stopwatch();

                    void ProgressAction(long current, long total)
                    {
                        bytesWrittenReported = current;
                        var percentage = total == 0 ? 100 : current * 100.0 / total;
                        var elapsed = writeStopwatch.Elapsed;
                        var speed = current / elapsed.TotalSeconds;
                        var speedStr = "N/A";
                        if (elapsed.TotalSeconds > 0.1)
                        {
                            speedStr = speed > 1024 * 1024 ? $"{speed / (1024 * 1024):F2} MiB/s" :
                                speed > 1024 ? $"{speed / 1024:F2} KiB/s" :
                                $"{speed:F0} B/s";
                        }

                        var fileDisplayString = $"Writing {label} ({filename}): ";
                        // Pad the display string to the calculated maximum width
                        var paddedFileDisplay = fileDisplayString.PadRight(maxFilenameDisplayLength);

                        // Format numbers for consistent width, e.g., percentage with 5 chars, MiB with 6 chars
                        var progressDetails = $"{percentage,5:F1}% ({current / (1024.0 * 1024.0),6:F2} / {total / (1024.0 * 1024.0),6:F2} MiB) [{speedStr,-10}]";

                        Console.Write($"\r{paddedFileDisplay}{progressDetails}    "); // Added extra spaces at the end to clear previous longer lines
                    }

                    bool success;
                    try
                    {
                        using var fileStream = imageFile.OpenRead();

                        writeStopwatch.Start();
                        success = await Task.Run(() => manager.Firehose.ProgramFromStream(
                            storageType,
                            targetLun,
                            globalOptions.Slot,
                            sectorSize,
                            (uint)resolvedStartSector,
                            numSectorsForXmlAttribute,
                            totalBytesToWriteIncludingPadding,
                            filename,
                            fileStream,
ProgressAction
                        ));
                        writeStopwatch.Stop();
                    }
                    catch (IOException ioEx)
                    {
                        Logging.Log($"IO Error reading input file '{imageFile.FullName}' (Label: {label}): {ioEx.Message}", LogLevel.Error);
                        Console.WriteLine();
                        continue;
                    }

                    Console.WriteLine(); // Newline after progress bar for this file

                    if (!success)
                    {
                        Logging.Log($"Failed to program '{filename}' (Label: {label}). Aborting 'rawprogram' for LUN {lunKey}.", LogLevel.Error);
                        return 1;
                    }
                    Logging.Log($"Successfully programmed '{filename}' (Label: {label}). {bytesWrittenReported / (1024.0 * 1024.0):F2} MiB in {writeStopwatch.Elapsed.TotalSeconds:F2}s.", LogLevel.Debug);
                } // End foreach progElement

                // Process corresponding patch file, if it exists
                if (patchFilesMap.TryGetValue(lunKey, out var patchFile))
                {
                    Logging.Log($"--- Patching LUN {lunKey} using {patchFile.Name} ---");
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
                    var patchIndex = 0;
                    foreach (var patchElement in patchElements)
                    {
                        patchIndex++;
                        var patchElementString = patchElement.ToString(SaveOptions.DisableFormatting);
                        var fullXmlPayload = $"<?xml version=\"1.0\" ?><data>{patchElementString}</data>";

                        Logging.Log($"Sending patch command {patchIndex}/{patchElements.Count} from {patchFile.Name}", LogLevel.Debug);
                        Logging.Log($"Patch XML: {patchElementString}", LogLevel.Trace);


                        var patchSuccess = await Task.Run(() => manager.Firehose.SendRawXmlAndGetResponse(fullXmlPayload));
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
                    Logging.Log($"Patching for LUN {lunKey} using {patchFile.Name} completed.", LogLevel.Debug);
                    Console.WriteLine();
                }
                else
                {
                    Logging.Log($"Note: patch{lunKey}.xml not found. Skipping patching for LUN {lunKey}.");
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
        finally
        {
            commandStopwatch.Stop();
            Logging.Log($"'rawprogram' command finished in {commandStopwatch.Elapsed.TotalSeconds:F2}s.");
        }

        Logging.Log("'rawprogram' command finished successfully.");
        return 0;
    }

    private static bool TryParseSectorExpression(string expression, ulong totalDiskSectorsForLun, out ulong resultSector)
    {
        resultSector = 0;
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        var trimmedExpression = expression.Trim();

        // Try direct ulong parse first
        if (ulong.TryParse(trimmedExpression, NumberStyles.Integer, CultureInfo.InvariantCulture, out resultSector))
        {
            return true;
        }
        // Try parsing after removing a potential trailing dot (from XML examples)
        if (trimmedExpression.EndsWith('.') && ulong.TryParse(trimmedExpression.AsSpan(0, trimmedExpression.Length - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out resultSector))
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
                    var op = match.Groups[1].Value;
                    if (ulong.TryParse(match.Groups[2].Value, out var operand))
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

                    Logging.Log($"Failed to parse operand '{match.Groups[2].Value}' in expression '{trimmedExpression}'.", LogLevel.Error); return false;
                }

                // Just "NUM_DISK_SECTORS"
                return true;
            }

            Logging.Log($"Unsupported NUM_DISK_SECTORS expression format: '{trimmedExpression}'.", LogLevel.Error);
            return false;
        }
        return false;
    }
}