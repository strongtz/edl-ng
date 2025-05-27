using Microsoft.Extensions.Logging;
using QCEDL.CLI.Commands;

namespace QCEDL.CLI.Logging;

internal static partial class Log
{
    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Executing 'rawprogram' command...")]
    internal static partial void ExecutingRawProgram(
        this ILogger<RawProgramCommand> logger);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Directory '{searchDir}' for pattern '{pattern}' not found.")]
    internal static partial void DirectoryNotFound(
        this ILogger<RawProgramCommand> logger,
        string searchDir,
        string pattern);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Found literal file: {literalFile}")]
    internal static partial void FoundLiteralFile(
        this ILogger<RawProgramCommand> logger,
        string literalFile);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "No files found matching pattern '{pattern}' in directory '{searchDir}'.")]
    internal static partial void NoFilesForPattern(
        this ILogger<RawProgramCommand> logger,
        string pattern,
        string searchDir);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Found file from pattern '{pattern}': {file}")]
    internal static partial void FoundPatternFile(
        this ILogger<RawProgramCommand> logger,
        string pattern,
        string file);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error enumerating files for pattern '{pattern}' in directory '{searchDir}'")]
    internal static partial void ErrorEnumeratingPattern(
        this ILogger<RawProgramCommand> logger,
        string pattern,
        string searchDir,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "No XML files found after resolving patterns.")]
    internal static partial void NoXmlFilesAfterResolve(
        this ILogger<RawProgramCommand> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Total unique XML files to process: {count}")]
    internal static partial void TotalUniqueXmlFiles(
        this ILogger<RawProgramCommand> logger,
        int count);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "XML file '{file}' not found.")]
    internal static partial void XmlFileNotFound(
        this ILogger<RawProgramCommand> logger,
        string file);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Duplicate rawprogram file for LUN {lun}: {first} and {second}. Using first one found.")]
    internal static partial void DuplicateRawProgramFile(
        this ILogger<RawProgramCommand> logger,
        int lun,
        string first,
        string second);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Duplicate patch file for LUN {lun}: {first} and {second}. Using first one found.")]
    internal static partial void DuplicatePatchFile(
        this ILogger<RawProgramCommand> logger,
        int lun,
        string first,
        string second);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Skipping file with unrecognized name format: {fileName}. Expected rawprogramN.xml or patchN.xml.")]
    internal static partial void SkippingUnrecognizedFile(
        this ILogger<RawProgramCommand> logger,
        string fileName);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "No rawprogramN.xml files found to process.")]
    internal static partial void NoRawProgramFilesFound(
        this ILogger<RawProgramCommand> logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "--- Processing LUN {lunKey} using {fileName} ---")]
    internal static partial void ProcessingLun(
        this ILogger<RawProgramCommand> logger,
        int lunKey,
        string fileName);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error parsing XML file '{filePath}'")]
    internal static partial void ErrorParsingXml(
        this ILogger<RawProgramCommand> logger,
        string filePath,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Invalid XML structure in '{filePath}': Root element must be <data>.")]
    internal static partial void InvalidXmlStructure(
        this ILogger<RawProgramCommand> logger,
        string filePath);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Found {count} <program> elements in {fileName}.")]
    internal static partial void FoundProgramElements(
        this ILogger<RawProgramCommand> logger,
        int count,
        string fileName);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Skipping <program> element with empty filename (Label: {label}).")]
    internal static partial void SkippingEmptyProgramFilename(
        this ILogger<RawProgramCommand> logger,
        string label);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message =
            "<program> element (Label: {label}) in {fileName} is missing required attributes (start_sector, SECTOR_SIZE_IN_BYTES, physical_partition_number).")]
    internal static partial void MissingProgramAttributes(
        this ILogger<RawProgramCommand> logger,
        string fileName,
        string label);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Invalid SECTOR_SIZE_IN_BYTES '{sectorSize}' for <program> (Label: {label}).")]
    internal static partial void InvalidSectorSize(
        this ILogger<RawProgramCommand> logger,
        string sectorSize,
        string label);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Invalid physical_partition_number '{partitionNumber}' for <program> (Label: {label}).")]
    internal static partial void InvalidPhysicalPartitionNumber(
        this ILogger<RawProgramCommand> logger,
        string partitionNumber,
        string label);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Fetching NUM_DISK_SECTORS for LUN {targetLun}...")]
    internal static partial void FetchingNumDiskSectors(
        this ILogger<RawProgramCommand> logger,
        uint targetLun);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "NUM_DISK_SECTORS (total_blocks) for LUN {targetLun} is invalid or zero.")]
    internal static partial void InvalidNumDiskSectors(
        this ILogger<RawProgramCommand> logger,
        uint targetLun);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Could not get storage info for LUN {targetLun} to resolve NUM_DISK_SECTORS.")]
    internal static partial void StorageInfoFetchError(
        this ILogger<RawProgramCommand> logger,
        uint targetLun,
        Exception exception);
    
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "NUM_DISK_SECTORS for LUN {targetLun} is {numDiskSectors}.")]
    internal static partial void ReportNumDiskSectors(
        this ILogger<RawProgramCommand> logger,
        uint targetLun,
        ulong numDiskSectors);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Could not parse start_sector expression '{expression}' for <program> (Label: {label}).")]
    internal static partial void ErrorParsingStartSectorExpression(
        this ILogger<RawProgramCommand> logger,
        string expression,
        string label);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Image file '{imagePath}' for <program> (Label: {label}) not found. Skipping this file.")]
    internal static partial void ImageFileNotFound(
        this ILogger<RawProgramCommand> logger,
        string imagePath,
        string label);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Image file '{imagePath}' (Label: {label}) is empty. Skipping write for this file.")]
    internal static partial void EmptyImageFile(
        this ILogger<RawProgramCommand> logger,
        string imagePath,
        string label);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Padding '{fileName}' (Label: {label}) from {originalLength} to {paddedLength} bytes (SectorSize: {sectorSize}).")]
    internal static partial void PaddingFile(
        this ILogger<RawProgramCommand> logger,
        string fileName,
        string label,
        long originalLength,
        long paddedLength,
        uint sectorSize);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Programming '{fileName}' (Label: {label}) to LUN {targetLun}, StartSector {startSector}, SectorSize {sectorSize}. Total to stream: {totalBytes} bytes.")]
    internal static partial void ProgrammingFile(
        this ILogger<RawProgramCommand> logger,
        string fileName,
        string label,
        uint targetLun,
        ulong startSector,
        uint sectorSize,
        long totalBytes);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "IO Error reading input file '{imagePath}' (Label: {label})")]
    internal static partial void IoErrorReadingInputFile(
        this ILogger<RawProgramCommand> logger,
        string imagePath,
        string label,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to program '{fileName}' (Label: {label}). Aborting 'rawprogram' for LUN {lunKey}.")]
    internal static partial void FailedProgrammingAndAbort(
        this ILogger<RawProgramCommand> logger,
        string fileName,
        string label,
        int lunKey);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Successfully programmed '{fileName}' (Label: {label}). {megabytes:F2} MiB in {elapsed}s.")]
    internal static partial void SuccessfullyProgrammedFile(
        this ILogger<RawProgramCommand> logger,
        string fileName,
        string label,
        double megabytes,
        TimeSpan elapsed);
    
      [LoggerMessage(
        Level = LogLevel.Information,
        Message = "--- Patching LUN {lunKey} using {patchFileName} ---")]
    internal static partial void PatchingStart(
        this ILogger<RawProgramCommand> logger,
        int lunKey,
        string patchFileName);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Found {count} <patch> elements in {patchFileName}.")]
    internal static partial void FoundPatchElements(
        this ILogger<RawProgramCommand> logger,
        int count,
        string patchFileName);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Sending patch command {index}/{total} from {patchFileName}")]
    internal static partial void SendingPatchCommand(
        this ILogger<RawProgramCommand> logger,
        int index,
        int total,
        string patchFileName);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Patch XML: {xmlContent}")]
    internal static partial void TracePatchXml(
        this ILogger<RawProgramCommand> logger,
        string xmlContent);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Patch command {index} ACKed.")]
    internal static partial void PatchCommandAcked(
        this ILogger<RawProgramCommand> logger,
        int index);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to send patch command {index} or received NAK.")]
    internal static partial void PatchCommandFailed(
        this ILogger<RawProgramCommand> logger,
        int index);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Aborting 'rawprogram'. {succeeded}/{total} patches succeeded before failure.")]
    internal static partial void AbortingRawprogram(
        this ILogger<RawProgramCommand> logger,
        int succeeded,
        int total);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Patching for LUN {lunKey} using {patchFileName} completed.")]
    internal static partial void PatchingCompleted(
        this ILogger<RawProgramCommand> logger,
        int lunKey,
        string patchFileName);
    
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Note: {patchFileName} not found. Skipping patching for LUN {lunKey}.")]
    internal static partial void PatchFileNotFound(
        this ILogger<RawProgramCommand> logger,
        string? patchFileName,
        int lunKey);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "--- Finished processing LUN {lunKey} ---")]
    internal static partial void FinishedProcessingLun(
        this ILogger<RawProgramCommand> logger,
        int lunKey);
    
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "'rawprogram' command finished in {elapsed}.")]
    internal static partial void RawProgramFinishedInTime(
        this ILogger<RawProgramCommand> logger,
        TimeSpan elapsed);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "'rawprogram' command finished successfully.")]
    internal static partial void RawProgramFinishedSuccessfully(
        this ILogger<RawProgramCommand> logger);
    
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "NUM_DISK_SECTORS ({resultSector}) - {operand} results in negative value.")]
    internal static partial void NumDiskSectorsNegative(
        this ILogger<RawProgramCommand> logger,
        ulong resultSector,
        ulong operand);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to parse operand '{operand}' in expression '{expression}'.")]
    internal static partial void FailedToParseOperand(
        this ILogger<RawProgramCommand> logger,
        string operand,
        string expression);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Unsupported NUM_DISK_SECTORS expression format: '{expression}'.")]
    internal static partial void UnsupportedNumDiskSectorsFormat(
        this ILogger<RawProgramCommand> logger,
        string expression);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Cannot resolve NUM_DISK_SECTORS because totalDiskSectorsForLun is 0.")]
    internal static partial void CannotResolveNumDiskSectorsZero(
        this ILogger<RawProgramCommand> logger);
}