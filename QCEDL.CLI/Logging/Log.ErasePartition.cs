using Microsoft.Extensions.Logging;
using QCEDL.CLI.Commands;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace QCEDL.CLI.Logging;

internal static partial class Log
{
       [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Executing 'erase-part' command: Partition '{partitionName}'...")]
    internal static partial void ExecutingErasePartition(
        this ILogger<ErasePartitionCommand> logger,
        string partitionName);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Using storage type: {storageType}")]
    internal static partial void UsingStorageType(
        this ILogger<ErasePartitionCommand> logger,
        StorageType storageType);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Scanning specified LUN: {specifiedLun}")]
    internal static partial void ScanningSpecifiedLun(
        this ILogger<ErasePartitionCommand> logger,
        uint? specifiedLun);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "No LUN specified, attempting to determine number of LUNs and scan all.")]
    internal static partial void ScanningAllLun(this ILogger<ErasePartitionCommand> logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not get device info to determine LUN count from LUN 0. Will try a default range.")]
    internal static partial void CannotGetLunCount(
        this ILogger<ErasePartitionCommand> logger,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Device reports {lunCount} LUN(s). Scanning LUNs: {luns}")]
    internal static partial void LunFound(
        this ILogger<ErasePartitionCommand> logger, int lunCount,
        IReadOnlyList<uint> luns);


    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not determine LUN count. Scanning default LUNs: {luns}")]
    internal static partial void ScanningDefaultLuns(
        this ILogger<ErasePartitionCommand> logger,
        IReadOnlyList<uint> luns);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Scanning LUN {currentLun} for partition '{partitionName}'...")]
    internal static partial void ScanningLun(
        this ILogger<ErasePartitionCommand> logger,
        uint currentLun,
        string partitionName);
    
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message =  "Could not get storage info for LUN {currentLun}. Skipping LUN.")]
    internal static partial void SkippingLun(
        this ILogger<ErasePartitionCommand> logger,
        uint currentLun,
        Exception exception);
    
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message =  "Storage info for LUN {currentLun} unreliable, using default sector size for {storageType}: {currentSectorSize}")]
    internal static partial void StorageInfoUnreliable(
        this ILogger<ErasePartitionCommand> logger,
        uint currentLun,
        StorageType storageType,
        uint currentSectorSize);
    
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message =  "Using sector size: {currentSectorSize} bytes for LUN {currentLun}.")]
    internal static partial void SectorSize(
        this ILogger<ErasePartitionCommand> logger,
        uint currentLun,
        uint currentSectorSize);
    
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message =  "Failed to read GPT area from LUN {currentLun}. Skipping LUN.")]
    internal static partial void FailedToReadGpt(
        this ILogger<ErasePartitionCommand> logger,
        uint currentLun,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message =  "Failed to read sufficient data for GPT from LUN {currentLun}.")]
    internal static partial void GptDataInsufficient(
        this ILogger<ErasePartitionCommand> logger,
        uint currentLun);
    
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Found partition '{partitionName}' on LUN {actualLun} with sector size {actualSectorSize}.")]
    internal static partial void FoundPartitionOnLun(
        this ILogger<ErasePartitionCommand> logger,
        string partitionName,
        uint actualLun,
        long actualSectorSize);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "  Details - Type: {typeGuid}, UID: {uid}, LBA: {firstLba}-{lastLba}")]
    internal static partial void PartitionDetails(
        this ILogger<ErasePartitionCommand> logger,
        Guid typeGuid,
        Guid uid,
        ulong firstLba,
        ulong lastLba);
    
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "No valid GPT found or parse error on LUN {currentLun}.")]
    internal static partial void GptNotFound(
        this ILogger<ErasePartitionCommand> logger,
        uint currentLun,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Error processing GPT on LUN {currentLun}.")]
    internal static partial void ErrorProcessingGpt(
        this ILogger<ErasePartitionCommand> logger,
        uint currentLun,
        Exception exception);
    
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Partition '{partitionName}' not found on {location}.")]
    internal static partial void PartitionNotFound(
        this ILogger<ErasePartitionCommand> logger,
        string partitionName,
        uint? location);
    
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Partition '{partitionName}' sector range (Start: {startSector}, Count: {sectorCount}) exceeds uint.MaxValue, which is not supported by the current Firehose.Erase implementation.")]
    internal static partial void PartitionSectorRangeTooLarge(
        this ILogger<ErasePartitionCommand> logger,
        string partitionName,
        ulong startSector,
        ulong sectorCount);
    
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Partition '{partitionName}' has zero size. Nothing to erase.")]
    internal static partial void PartitionZeroSize(
        this ILogger<ErasePartitionCommand> logger,
        string partitionName);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Attempting to erase partition '{partitionName}' (LUN {actualLun}, LBA {startSector}, {sectorsCount} sectors)...")]
    internal static partial void ErasingPartition(
        this ILogger<ErasePartitionCommand> logger,
        string partitionName,
        uint actualLun,
        ulong startSector,
        ulong sectorsCount);
    
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Successfully erased partition '{partitionName}' in {elapsedSeconds:F2}s.")]
    internal static partial void PartitionErased(
        this ILogger<ErasePartitionCommand> logger,
        string partitionName,
        double elapsedSeconds);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to erase partition '{partitionName}'. Check previous logs.")]
    internal static partial void PartitionEraseFailed(
        this ILogger<ErasePartitionCommand> logger,
        string partitionName);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "'erase-part' command finished in {elapsed}.")]
    internal static partial void ErasePartCommandFinished(
        this ILogger<ErasePartitionCommand> logger,
        TimeSpan elapsed);
    
}