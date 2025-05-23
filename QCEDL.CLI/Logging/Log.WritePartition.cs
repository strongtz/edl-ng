using Microsoft.Extensions.Logging;
using QCEDL.CLI.Commands;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace QCEDL.CLI.Logging;

internal static partial class Log
{
    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Executing 'write-partition' command: Partition '{partitionName}', File '{filePath}'...")]
    internal static partial void ExecutingWritePartition(
        this ILogger<WritePartitionCommand> logger,
        string partitionName,
        string filePath);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Input file '{filePath}' not found.")]
    internal static partial void InputFileNotFound(
        this ILogger<WritePartitionCommand> logger,
        string filePath);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Using storage type: {storageType}")]
    internal static partial void UsingStorageType(
        this ILogger<WritePartitionCommand> logger,
        StorageType storageType);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Input file is empty. Nothing to write.")]
    internal static partial void InputFileEmpty(
        this ILogger<WritePartitionCommand> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Scanning specified LUN: {lun}")]
    internal static partial void ScanningSpecifiedLun(
        this ILogger<WritePartitionCommand> logger,
        uint lun);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "No LUN specified, attempting to determine number of LUNs and scan all.")]
    internal static partial void NoLunSpecified(
        this ILogger<WritePartitionCommand> logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not get device info to determine LUN count from LUN 0. Will try a default range.")]
    internal static partial void CouldNotGetDeviceInfo(
        this ILogger<WritePartitionCommand> logger,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Device reports {count} LUN(s). Scanning LUNs: {luns}")]
    internal static partial void DeviceReportsNumPhysicalLuns(
        this ILogger<WritePartitionCommand> logger,
        int count,
        IReadOnlyList<uint> luns);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not determine LUN count. Scanning default LUNs: {luns}")]
    internal static partial void CouldNotDetermineLunCount(
        this ILogger<WritePartitionCommand> logger,
        IReadOnlyList<uint> luns);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Scanning LUN {lun} for partition '{partitionName}'...")]
    internal static partial void ScanningLunForPartition(
        this ILogger<WritePartitionCommand> logger,
        uint lun,
        string partitionName);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not get storage info for LUN {lun}. Skipping LUN.")]
    internal static partial void CouldNotGetStorageInfo(
        this ILogger<WritePartitionCommand> logger,
        uint lun,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Storage info for LUN {lun} unreliable, using default sector size for {storageType}: {sectorSize}")]
    internal static partial void StorageInfoUnreliable(
        this ILogger<WritePartitionCommand> logger,
        uint lun,
        StorageType storageType,
        uint sectorSize);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Using sector size: {sectorSize} bytes for LUN {lun}.")]
    internal static partial void UsingSectorSize(
        this ILogger<WritePartitionCommand> logger,
        uint sectorSize,
        uint lun);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to read GPT area from LUN {lun}. Skipping LUN.")]
    internal static partial void FailedToReadGptArea(
        this ILogger<WritePartitionCommand> logger,
        uint lun,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to read sufficient data for GPT from LUN {lun}.")]
    internal static partial void FailedToReadGptData(
        this ILogger<WritePartitionCommand> logger,
        uint lun);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Found partition '{partitionName}' on LUN {lun} with sector size {sectorSize}.")]
    internal static partial void FoundPartition(
        this ILogger<WritePartitionCommand> logger,
        string partitionName,
        uint lun,
        uint sectorSize);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "  Details - Type: {typeGuid}, UID: {uid}, LBA: {firstLba}-{lastLba}")]
    internal static partial void PartitionDetails(
        this ILogger<WritePartitionCommand> logger,
        Guid typeGuid,
        Guid uid,
        ulong firstLba,
        ulong lastLba);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "No valid GPT found or parse error on LUN {lun}.")]
    internal static partial void NoValidGptOrParseError(
        this ILogger<WritePartitionCommand> logger,
        uint lun,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Error processing GPT on LUN {lun}.")]
    internal static partial void ErrorProcessingGpt(
        this ILogger<WritePartitionCommand> logger,
        uint lun,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Partition '{partitionName}' not found on LUN {lun}.")]
    internal static partial void PartitionNotFoundOnLun(
        this ILogger<WritePartitionCommand> logger,
        string partitionName,
        uint? lun);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message =
            "Input file size ({originalFileLength} bytes) is larger than the partition '{partitionName}' size ({partitionSizeInBytes} bytes).")]
    internal static partial void InputFileLargerThanPartition(
        this ILogger<WritePartitionCommand> logger,
        long originalFileLength,
        string partitionName,
        long partitionSizeInBytes);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message =
            "Input file size ({originalFileLength} bytes) is not a multiple of partition's sector size ({actualSectorSize} bytes). Will pad with zeros to {totalBytesToWriteIncludingPadding} bytes.")]
    internal static partial void InputFilePaddingWarning(
        this ILogger<WritePartitionCommand> logger,
        long originalFileLength,
        uint actualSectorSize,
        long totalBytesToWriteIncludingPadding);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message =
            "Padded data size ({totalBytesToWriteIncludingPadding} bytes) would be larger than the partition '{partitionName}' size ({partitionSizeInBytes} bytes). This should not happen if original file fits.")]
    internal static partial void PaddedDataLargerThanPartition(
        this ILogger<WritePartitionCommand> logger,
        long totalBytesToWriteIncludingPadding,
        string partitionName,
        long partitionSizeInBytes);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message =
            "Data to write: {originalFileLength} bytes from file, padded to {totalBytesToWriteIncludingPadding} bytes ({numSectorsForXml} sectors).")]
    internal static partial void DataToWriteWithPadding(
        this ILogger<WritePartitionCommand> logger,
        long originalFileLength,
        long totalBytesToWriteIncludingPadding,
        uint numSectorsForXml);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message =
            "Padded data size is smaller than partition size. The remaining space in partition '{partitionName}' will not be explicitly overwritten or zeroed out by this operation beyond the {totalBytesToWriteIncludingPadding} bytes written.")]
    internal static partial void PaddedDataSmallerThanPartition(
        this ILogger<WritePartitionCommand> logger,
        string partitionName,
        long totalBytesToWriteIncludingPadding);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message =
            "Partition start LBA ({partStartSector}) exceeds uint.MaxValue, not supported by current Firehose.ProgramFromStream.")]
    internal static partial void PartitionStartLbaExceedsMaxValue(
        this ILogger<WritePartitionCommand> logger,
        ulong partStartSector);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message =
            "Attempting to write {totalBytesToWriteIncludingPadding} bytes to partition '{partitionName}' (LUN {actualLun}, LBA {partStartSector})...")]
    internal static partial void AttemptingPartitionWrite(
        this ILogger<WritePartitionCommand> logger,
        long totalBytesToWriteIncludingPadding,
        string partitionName,
        uint actualLun,
        ulong partStartSector);
    
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Data ({size:F2} MiB) successfully written to partition '{partitionName}' in {elapsed}.")]
    internal static partial void WritePartitionSucceeded(
        this ILogger<WritePartitionCommand> logger,
        double size,
        string partitionName,
        TimeSpan elapsed);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to write data to partition '{partitionName}'. Check previous logs.")]
    internal static partial void WritePartitionFailed(
        this ILogger<WritePartitionCommand> logger,
        string partitionName);
    
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "IO Error reading input file '{filePath}': {errorMessage}")]
    internal static partial void IoErrorReadingInputFile(
        this ILogger<WritePartitionCommand> logger,
        string filePath,
        string errorMessage);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "'write-part' command finished in {elapsedSeconds:F2}s.")]
    internal static partial void WritePartFinished(
        this ILogger<WritePartitionCommand> logger,
        double elapsedSeconds);
}