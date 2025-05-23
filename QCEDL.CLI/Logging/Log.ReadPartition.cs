using Microsoft.Extensions.Logging;
using QCEDL.CLI.Commands;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace QCEDL.CLI.Logging;

internal static partial class Log
{
    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Executing 'read-part' command: Partition '{partitionName}', File '{filePath}'...")]
    internal static partial void ExecutingReadPartCommand(
        this ILogger<ReadPartitionCommand> logger,
        string partitionName,
        string filePath);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Using storage type: {storageType}")]
    internal static partial void UsingStorageType(
        this ILogger<ReadPartitionCommand> logger,
        StorageType storageType);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Scanning specified LUN: {lun}")]
    internal static partial void ScanningSpecifiedLun(
        this ILogger<ReadPartitionCommand> logger,
        uint lun);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "No LUN specified, attempting to determine number of LUNs and scan all.")]
    internal static partial void NoLunSpecifiedScanAll(
        this ILogger<ReadPartitionCommand> logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not get device info to determine LUN count from LUN 0. Will try a default range.")]
    internal static partial void CouldNotGetDeviceInfoForLun0(
        this ILogger<ReadPartitionCommand> logger,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Device reports {numPhysical} LUN(s). Scanning LUNs: {luns}.")]
    internal static partial void DeviceReportsAndScanningLuns(
        this ILogger<ReadPartitionCommand> logger,
        int numPhysical,
        IReadOnlyList<uint> luns);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not determine LUN count. Scanning default LUNs: {luns}.")]
    internal static partial void CouldNotDetermineLunCountDefault(
        this ILogger<ReadPartitionCommand> logger,
        IReadOnlyList<uint> luns);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Scanning LUN {lun} for partition '{partitionName}'...")]
    internal static partial void ScanningLunForPartition(
        this ILogger<ReadPartitionCommand> logger,
        uint lun,
        string partitionName);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not get storage info for LUN {lun}. Skipping LUN.")]
    internal static partial void CouldNotGetStorageInfoForLun(
        this ILogger<ReadPartitionCommand> logger,
        uint lun,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Storage info for LUN {lun} unreliable, using default sector size for {storageType}: {sectorSize}")]
    internal static partial void StorageInfoUnreliableUseDefaultSectorSize(
        this ILogger<ReadPartitionCommand> logger,
        uint lun,
        StorageType storageType,
        uint sectorSize);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Using sector size: {sectorSize} bytes for LUN {lun}.")]
    internal static partial void UsingSectorSizeForLun(
        this ILogger<ReadPartitionCommand> logger,
        uint lun,
        uint sectorSize);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to read GPT area from LUN {lun}. Skipping LUN.")]
    internal static partial void FailedToReadGptAreaForLun(
        this ILogger<ReadPartitionCommand> logger,
        uint lun,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to read sufficient data for GPT from LUN {lun}.")]
    internal static partial void FailedToReadSufficientGptData(
        this ILogger<ReadPartitionCommand> logger,
        uint lun);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Found partition '{partitionName}' on LUN {lun} with sector size {sectorSize}.")]
    internal static partial void FoundPartitionOnLun(
        this ILogger<ReadPartitionCommand> logger,
        string partitionName,
        uint lun,
        uint sectorSize);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "  Details - Type: {typeGuid}, UID: {uid}, LBA: {firstLba}-{lastLba}")]
    internal static partial void PartitionDetails(
        this ILogger<ReadPartitionCommand> logger,
        Guid typeGuid,
        Guid uid,
        ulong firstLba,
        ulong lastLba);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "No valid GPT found or parse error on LUN {lun}.")]
    internal static partial void NoValidGptFound(
        this ILogger<ReadPartitionCommand> logger,
        uint lun);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Error processing GPT on LUN {lun}")]
    internal static partial void ErrorProcessingGpt(
        this ILogger<ReadPartitionCommand> logger,
        uint lun,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Partition '{partitionName}' not found on LUN {lunInfo}")]
    internal static partial void PartitionNotFound(
        this ILogger<ReadPartitionCommand> logger,
        string partitionName,
        uint? lunInfo);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message =
            "Partition sector range (LBA {startLba}-{endLba}) exceeds uint.MaxValue, which is not supported by the current Firehose.Read implementation.")]
    internal static partial void PartitionRangeExceedsUint(
        this ILogger<ReadPartitionCommand> logger,
        ulong startLba,
        ulong endLba);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Partition '{partitionName}' has zero or negative size ({bytes} bytes). Nothing to read.")]
    internal static partial void PartitionSizeZeroOrNegative(
        this ILogger<ReadPartitionCommand> logger,
        string partitionName,
        long bytes);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message =
            "Preparing to read partition '{partitionName}' from LUN {lun}: LBA {startLba} to {endLba} ({numSectors} sectors, {bytes} bytes) into '{filePath}'...")]
    internal static partial void PreparingToReadPartition(
        this ILogger<ReadPartitionCommand> logger,
        string partitionName,
        uint lun,
        ulong startLba,
        ulong endLba,
        ulong numSectors,
        long bytes,
        string filePath);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "IO Error creating/writing to file '{filePath}': {errorMessage}")]
    internal static partial void IoErrorWritingFile(
        this ILogger<ReadPartitionCommand> logger,
        string filePath,
        string errorMessage);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not delete partial file '{filePath}'")]
    internal static partial void CouldNotDeletePartialFile(
        this ILogger<ReadPartitionCommand> logger,
        string filePath,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Successfully read {miB:F2} MiB and wrote to '{filePath}' in {seconds:F2}s.")]
    internal static partial void SuccessfullyReadAndWrote(
        this ILogger<ReadPartitionCommand> logger,
        double miB,
        string filePath,
        double seconds);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to read partition '{partitionName}' or write to stream.")]
    internal static partial void FailedReadPartitionOrWrite(
        this ILogger<ReadPartitionCommand> logger,
        string partitionName);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "'read-part' command finished in {elapsed}.")]
    internal static partial void ReadPartCommandFinished(
        this ILogger<ReadPartitionCommand> logger,
        TimeSpan elapsed);
}