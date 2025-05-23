using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using QCEDL.CLI.Core;
using QCEDL.CLI.Logging;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.JSON.StorageInfo;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace QCEDL.CLI.Commands;

internal sealed partial class RawProgramCommand(
    ILogger<RawProgramCommand> logger,
    GlobalOptionsBinder globalOptionsBinder,
    IEdlManagerProvider edlManagerProvider) : ICommand
{
    private static readonly Argument<string[]> XmlFilePatternsArgument =
        new("xmlfile_patterns",
            "Paths or patterns for rawprogram and patch XML files (e.g., rawprogram0.xml patch0.xml rawprogram*.xml patch*.xml).")
        {
            Arity = ArgumentArity.OneOrMore
        };

    public Command Create()
    {
        var command = new Command("rawprogram", "Processes rawprogramN.xml and patchN.xml files for flashing.")
        {
            XmlFilePatternsArgument // Use the new argument
        };

        command.SetHandler(
            ExecuteAsync,
            globalOptionsBinder,
            XmlFilePatternsArgument);

        return command;
    }

    private async Task<int> ExecuteAsync(GlobalOptionsBinder globalOptions, string[] xmlFilePatterns)
    {
        logger.ExecutingRawProgram();
        var commandStopwatch = Stopwatch.StartNew();

        List<FileInfo> resolvedXmlFiles = [];
        var currentDirectory = Environment.CurrentDirectory;

        foreach (var pattern in xmlFilePatterns)
        {
            var dirName = Path.GetDirectoryName(pattern);
            var fileNamePattern = Path.GetFileName(pattern);

            var searchDir = string.IsNullOrEmpty(dirName)
                ? currentDirectory
                : Path.GetFullPath(Path.Combine(currentDirectory, dirName));

            if (!Directory.Exists(searchDir))
            {
                logger.DirectoryNotFound(searchDir, pattern);
                return 1;
            }

            try
            {
                var foundFiles = Directory.EnumerateFiles(searchDir, fileNamePattern, SearchOption.TopDirectoryOnly)
                    .ToArray();
                if (foundFiles.Length == 0)
                {
                    // If no files found by globbing, check if the pattern itself is a literal file path that exists
                    var literalFile = new FileInfo(Path.Combine(searchDir, fileNamePattern));
                    if (literalFile.Exists)
                    {
                        resolvedXmlFiles.Add(literalFile);
                        logger.FoundLiteralFile(literalFile.FullName);
                    }
                    else
                    {
                        logger.NoFilesForPattern(pattern, searchDir);
                    }
                }
                else
                {
                    foreach (var file in foundFiles)
                    {
                        resolvedXmlFiles.Add(new(file));
                        logger.FoundPatternFile(pattern, file);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.ErrorEnumeratingPattern(pattern, searchDir, ex);
                return 1;
            }
        }

        if (resolvedXmlFiles.Count == 0)
        {
            logger.NoXmlFilesAfterResolve();
            return 1;
        }

        // Deduplicate in case patterns overlap or literal files are also matched by patterns
        resolvedXmlFiles = resolvedXmlFiles.DistinctBy(f => f.FullName).ToList();
        logger.TotalUniqueXmlFiles(resolvedXmlFiles.Count);

        // Validate all files exist
        foreach (var file in resolvedXmlFiles)
        {
            if (!file.Exists)
            {
                logger.XmlFileNotFound(file.FullName);
                return 1;
            }
        }

        var rawProgramFilesMap = new Dictionary<int, FileInfo>();
        var patchFilesMap = new Dictionary<int, FileInfo>();
        var rawProgramRegex = RawProgramRegex();
        var patchRegex = PatchRegex();

        foreach (var file in resolvedXmlFiles)
        {
            var rawMatch = rawProgramRegex.Match(file.Name);
            if (rawMatch.Success && int.TryParse(rawMatch.Groups[1].Value, out var lun))
            {
                if (rawProgramFilesMap.TryGetValue(lun, out var value))
                {
                    logger.DuplicateRawProgramFile(lun, file.Name, value.Name);
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
                        logger.DuplicatePatchFile(lunPatch, file.Name, value.Name);
                    }
                    else
                    {
                        patchFilesMap[lunPatch] = file;
                    }
                }
                else
                {
                    logger.SkippingUnrecognizedFile(file.Name);
                }
            }
        }

        var sortedLunsToProcess = rawProgramFilesMap.Keys.OrderBy(k => k).ToList();

        if (sortedLunsToProcess.Count == 0)
        {
            logger.NoRawProgramFilesFound();
            return 1;
        }

        var storageType = globalOptions.MemoryType ?? StorageType.UFS;
        var lunTotalSectorsCache = new Dictionary<uint, ulong>();

        try
        {
            using var manager = edlManagerProvider.CreateEdlManager();
            await manager.EnsureFirehoseModeAsync();
            await manager.ConfigureFirehoseAsync(); // Initial configure

            foreach (var lunKey in sortedLunsToProcess)
            {
                var rawFile = rawProgramFilesMap[lunKey];
                logger.ProcessingLun(lunKey, rawFile.Name);

                XDocument rawDoc;
                try
                {
                    rawDoc = XDocument.Load(rawFile.FullName);
                }
                catch (Exception ex)
                {
                    logger.ErrorParsingXml(rawFile.FullName, ex);
                    return 1; // Abort on XML error
                }

                if (rawDoc.Root == null || rawDoc.Root.Name != "data")
                {
                    logger.InvalidXmlStructure(rawFile.FullName);
                    return 1;
                }

                var programElements = rawDoc.Root.Elements("program").ToList();
                logger.FoundProgramElements(programElements.Count, rawFile.Name);

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
                        logger.SkippingEmptyProgramFilename(label);
                        continue;
                    }

                    var startSectorStr = progElement.Attribute("start_sector")?.Value;
                    var sectorSizeStr = progElement.Attribute("SECTOR_SIZE_IN_BYTES")?.Value;
                    var physicalPartitionNumberStr = progElement.Attribute("physical_partition_number")?.Value;

                    if (string.IsNullOrEmpty(startSectorStr) || string.IsNullOrEmpty(sectorSizeStr) ||
                        string.IsNullOrEmpty(physicalPartitionNumberStr))
                    {
                        logger.MissingProgramAttributes(rawFile.Name, label);
                        continue;
                    }

                    if (!uint.TryParse(sectorSizeStr, out var sectorSize) || sectorSize == 0)
                    {
                        logger.InvalidSectorSize(sectorSizeStr, label);
                        continue;
                    }

                    if (!uint.TryParse(physicalPartitionNumberStr, out var targetLun))
                    {
                        logger.InvalidPhysicalPartitionNumber(physicalPartitionNumberStr, label);
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
                            logger.FetchingNumDiskSectors(targetLun);
                            Root? storageInfo = null;
                            try
                            {
                                storageInfo = await Task.Run(() =>
                                    manager.Firehose.GetStorageInfo(storageType, targetLun, globalOptions.Slot));
                            }
                            catch (Exception ex)
                            {
                                logger.StorageInfoFetchError(targetLun, ex);
                                return 1; // Cannot proceed if NUM_DISK_SECTORS is needed but unavailable
                            }

                            if (storageInfo?.StorageInfo?.TotalBlocks <= 0)
                            {
                                logger.InvalidNumDiskSectors(targetLun);
                                return 1;
                            }

                            numDiskSectorsForTargetLun = (ulong)(storageInfo?.StorageInfo?.TotalBlocks ?? 0);
                            lunTotalSectorsCache[targetLun] = numDiskSectorsForTargetLun;
                            logger.ReportNumDiskSectors(targetLun, numDiskSectorsForTargetLun);
                        }
                    }

                    if (!TryParseSectorExpression(startSectorStr, numDiskSectorsForTargetLun,
                            out var resolvedStartSector))
                    {
                        logger.ErrorParsingStartSectorExpression(startSectorStr, label);
                        return 1;
                    }

                    var imageFile = new FileInfo(Path.Combine(rawFile.DirectoryName ?? "", filename));
                    if (!imageFile.Exists)
                    {
                        logger.ImageFileNotFound(imageFile.FullName, label);
                        continue;
                    }

                    if (imageFile.Length == 0 && !string.Equals(filename, "ZERO", StringComparison.OrdinalIgnoreCase))
                    {
                        logger.EmptyImageFile(imageFile.FullName, label);
                        continue;
                    }

                    var originalFileLength = imageFile.Length;
                    long totalBytesToWriteIncludingPadding;

                    var remainder = originalFileLength % sectorSize;
                    if (remainder != 0)
                    {
                        totalBytesToWriteIncludingPadding = originalFileLength + (sectorSize - remainder);
                        logger.PaddingFile(imageFile.Name, label, originalFileLength, totalBytesToWriteIncludingPadding,
                            sectorSize);
                    }
                    else
                    {
                        totalBytesToWriteIncludingPadding = originalFileLength;
                    }

                    var numSectorsForXmlAttribute =
                        (uint)(totalBytesToWriteIncludingPadding /
                               sectorSize); // This is what goes into the <program num_partition_sectors="..."> attribute

                    logger.ProgrammingFile(
                        imageFile.Name,
                        label,
                        targetLun,
                        resolvedStartSector,
                        sectorSize,
                        totalBytesToWriteIncludingPadding);

                    long bytesWrittenReported = 0;
                    var writeStopwatch = new Stopwatch();

                    Action<long, long> progressAction = (current, total) =>
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
                        var progressDetails =
                            $"{percentage,5:F1}% ({current / (1024.0 * 1024.0),6:F2} / {total / (1024.0 * 1024.0),6:F2} MiB) [{speedStr,-10}]";

                        Console.Write(
                            $"\r{paddedFileDisplay}{progressDetails}    "); // Added extra spaces at the end to clear previous longer lines
                    };

                    bool success;
                    try
                    {
                        await using var fileStream = imageFile.OpenRead();

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
                            progressAction
                        ));
                        writeStopwatch.Stop();
                    }
                    catch (IOException ioEx)
                    {
                        logger.IoErrorReadingInputFile(imageFile.FullName, label, ioEx);
                        Console.WriteLine();
                        continue;
                    }

                    Console.WriteLine(); // Newline after progress bar for this file

                    if (!success)
                    {
                        logger.FailedProgrammingAndAbort(filename, label, lunKey);
                        return 1;
                    }

                    logger.SuccessfullyProgrammedFile(filename, label, bytesWrittenReported / (1024.0 * 1024.0),
                        writeStopwatch.Elapsed);
                } // End foreach progElement

                // Process corresponding patch file, if it exists
                if (patchFilesMap.TryGetValue(lunKey, out var patchFile))
                {
                    logger.PatchingStart(lunKey, patchFile.Name);
                    XDocument patchDoc;
                    try
                    {
                        patchDoc = XDocument.Load(patchFile.FullName);
                    }
                    catch (Exception ex)
                    {
                        logger.ErrorParsingXml(patchFile.FullName, ex);
                        return 1; // Abort on XML error
                    }

                    if (patchDoc.Root == null || patchDoc.Root.Name != "patches")
                    {
                        logger.InvalidXmlStructure(patchFile.FullName);
                        return 1;
                    }

                    var patchElements = patchDoc.Root.Elements("patch").ToList();
                    logger.FoundPatchElements(patchElements.Count, patchFile.Name);
                    var patchIndex = 0;
                    foreach (var patchElement in patchElements)
                    {
                        patchIndex++;
                        var patchElementString = patchElement.ToString(SaveOptions.DisableFormatting);
                        var fullXmlPayload = $"<?xml version=\"1.0\" ?><data>{patchElementString}</data>";

                        logger.SendingPatchCommand(patchIndex, patchElements.Count, patchFile.Name);
                        logger.TracePatchXml(patchElementString);

                        var patchSuccess =
                            await Task.Run(() => manager.Firehose.SendRawXmlAndGetResponse(fullXmlPayload));
                        if (patchSuccess)
                        {
                            logger.PatchCommandAcked(patchIndex);
                        }
                        else
                        {
                            logger.PatchCommandFailed(patchIndex);
                            logger.AbortingRawprogram(patchIndex - 1, patchElements.Count);
                            return 1;
                        }
                    }

                    logger.PatchingCompleted(lunKey, patchFile.Name);
                    Console.WriteLine();
                }
                else
                {
                    logger.PatchFileNotFound(patchFile?.Name, lunKey);
                }

                logger.FinishedProcessingLun(lunKey);
            }
        }
        catch (FileNotFoundException ex)
        {
            logger.ExceptedException(ex);
            return 1;
        }
        catch (ArgumentException ex)
        {
            logger.ExceptedException(ex);
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            logger.ExceptedException(ex);
            return 1;
        }
        catch (IOException ex)
        {
            logger.ExceptedException(ex);
            return 1;
        }
        catch (Exception ex)
        {
            logger.UnexceptedException(ex);
            return 1;
        }
        finally
        {
            commandStopwatch.Stop();
            logger.RawProgramFinishedInTime(commandStopwatch.Elapsed);
        }

        logger.RawProgramFinishedSuccessfully();
        return 0;
    }

    private bool TryParseSectorExpression(string expression, ulong totalDiskSectorsForLun, out ulong resultSector)
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
        if (trimmedExpression.EndsWith('.') && ulong.TryParse(trimmedExpression.AsSpan(0, trimmedExpression.Length - 1),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out resultSector))
        {
            return true;
        }

        // Handle NUM_DISK_SECTORS expressions
        if (trimmedExpression.Contains("NUM_DISK_SECTORS"))
        {
            if (totalDiskSectorsForLun == 0) // Not fetched or invalid
            {
                logger.CannotResolveNumDiskSectorsZero();
                return false;
            }

            // Regex for "NUM_DISK_SECTORS" optionally followed by "- X" or "+ X"
            // Example: "NUM_DISK_SECTORS - 5" or "NUM_DISK_SECTORS-5."
            var match = Regex.Match(trimmedExpression, @"^\s*NUM_DISK_SECTORS\s*(?:([+-])\s*(\d+))?\s*\.?\s*$",
                RegexOptions.IgnoreCase);
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
                            if (resultSector < operand)
                            {
                                logger.NumDiskSectorsNegative(resultSector, operand);
                                return false;
                            }

                            resultSector -= operand;
                        }
                        else // op == "+"
                        {
                            resultSector += operand;
                        }

                        return true;
                    }

                    logger.FailedToParseOperand(match.Groups[2].Value, trimmedExpression);
                }

                // Just "NUM_DISK_SECTORS"
                return true;
            }

            logger.UnsupportedNumDiskSectorsFormat(trimmedExpression);
        }

        return false;
    }

    [GeneratedRegex(@"rawprogram(\d+)\.xml$", RegexOptions.IgnoreCase)]
    private static partial Regex RawProgramRegex();

    [GeneratedRegex(@"patch(\d+)\.xml$", RegexOptions.IgnoreCase, "zh-CN")]
    private static partial Regex PatchRegex();
}