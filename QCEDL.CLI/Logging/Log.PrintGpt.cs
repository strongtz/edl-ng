using Microsoft.Extensions.Logging;
using QCEDL.CLI.Commands;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace QCEDL.CLI.Logging;

internal static partial class Log
{
    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Executing 'printgpt' command...")]
    internal static partial void ExecutingPrintGpt(
        this ILogger<PrintGptCommand> logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Attempting to read GPT from LUN {lun}...")]
    internal static partial void AttemptReadGpt(
        this ILogger<PrintGptCommand> logger,
        uint lun);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Could not get storage info for LUN {lun} (StorageType: {storageType}). Using default sector size.")]
    internal static partial void StorageInfoError(
        this ILogger<PrintGptCommand> logger,
        uint lun,
        StorageType storageType,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Storage info unreliable, using default sector size for {storageType}: {sectorSize}")]
    internal static partial void DefaultSectorSizeWithType(
        this ILogger<PrintGptCommand> logger,
        StorageType storageType,
        uint sectorSize);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Storage info unreliable, using default sector size: {sectorSize}")]
    internal static partial void DefaultSectorSize(
        this ILogger<PrintGptCommand> logger,
        uint sectorSize);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Using sector size: {sectorSize} bytes for LUN {lun}.")]
    internal static partial void UsingSectorSize(
        this ILogger<PrintGptCommand> logger,
        uint sectorSize,
        uint lun);

     [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to read sufficient data for GPT from LUN {lun}.")]
    internal static partial void FailedToReadSufficientGpt(
        this ILogger<PrintGptCommand> logger,
        uint lun);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "No valid GPT found on LUN {lun}.")]
    internal static partial void NoValidGptFound(
        this ILogger<PrintGptCommand> logger,
        uint lun);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "--- GPT Header LUN {lun} ---")]
    internal static partial void GptHeaderSection(
        this ILogger<PrintGptCommand> logger,
        uint lun);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Signature: {signature:X}")]
    internal static partial void GptSignature(
        this ILogger<PrintGptCommand> logger,
        ulong signature);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Revision: {revision:X8}")]
    internal static partial void GptRevision(
        this ILogger<PrintGptCommand> logger,
        uint revision);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Header Size: {size}")]
    internal static partial void GptHeaderSize(
        this ILogger<PrintGptCommand> logger,
        uint size);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Header CRC32: {crc32:X8}")]
    internal static partial void GptHeaderCrc32(
        this ILogger<PrintGptCommand> logger,
        uint crc32);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Current LBA: {currentLba}")]
    internal static partial void GptCurrentLba(
        this ILogger<PrintGptCommand> logger,
        ulong currentLba);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Backup LBA: {backupLba}")]
    internal static partial void GptBackupLba(
        this ILogger<PrintGptCommand> logger,
        ulong backupLba);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "First Usable LBA: {firstUsableLba}")]
    internal static partial void GptFirstUsableLba(
        this ILogger<PrintGptCommand> logger,
        ulong firstUsableLba);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Last Usable LBA: {lastUsableLba}")]
    internal static partial void GptLastUsableLba(
        this ILogger<PrintGptCommand> logger,
        ulong lastUsableLba);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Disk GUID: {diskGuid}")]
    internal static partial void GptDiskGuid(
        this ILogger<PrintGptCommand> logger,
        Guid diskGuid);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Partition Array LBA: {partitionArrayLba}")]
    internal static partial void GptPartitionArrayLba(
        this ILogger<PrintGptCommand> logger,
        ulong partitionArrayLba);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Partition Entry Count: {partitionEntryCount}")]
    internal static partial void GptPartitionEntryCount(
        this ILogger<PrintGptCommand> logger,
        uint partitionEntryCount);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Partition Entry Size: {partitionEntrySize}")]
    internal static partial void GptPartitionEntrySize(
        this ILogger<PrintGptCommand> logger,
        uint partitionEntrySize);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Partition Array CRC32: {partitionArrayCrc32:X8}")]
    internal static partial void GptPartitionArrayCrc32(
        this ILogger<PrintGptCommand> logger,
        uint partitionArrayCrc32);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Is Backup GPT: {isBackup}")]
    internal static partial void GptIsBackup(
        this ILogger<PrintGptCommand> logger,
        bool isBackup);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "--- Partitions LUN {lun} ---")]
    internal static partial void GptPartitionsSection(
        this ILogger<PrintGptCommand> logger,
        uint lun);
    
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "No partitions found in GPT.")]
    internal static partial void NoPartitionsFound(
        this ILogger<PrintGptCommand> logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "  Name: {partitionName}")]
    internal static partial void PartitionName(
        this ILogger<PrintGptCommand> logger,
        string partitionName);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "    Type: {typeGuid}")]
    internal static partial void PartitionTypeGuid(
        this ILogger<PrintGptCommand> logger,
        Guid typeGuid);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "    UID:  {uid}")]
    internal static partial void PartitionUid(
        this ILogger<PrintGptCommand> logger,
        Guid uid);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "    LBA:  {firstLba}-{lastLba} (Size: {sizeMiB:F2} MiB)")]
    internal static partial void PartitionLba(
        this ILogger<PrintGptCommand> logger,
        ulong firstLba,
        ulong lastLba,
        double sizeMiB);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "    Attr: {attributes:X16}")]
    internal static partial void PartitionAttributes(
        this ILogger<PrintGptCommand> logger,
        ulong attributes);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error parsing GPT data from LUN {lun}: {errorMessage}")]
    internal static partial void ErrorParsingGptData(
        this ILogger<PrintGptCommand> logger,
        uint lun,
        string errorMessage);
}