using Microsoft.Extensions.Logging;
using QCEDL.CLI.Commands;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace QCEDL.CLI.Logging;

internal static partial class Log
{
    [LoggerMessage(
        Level = LogLevel.Trace,
        Message =
            "Executing 'read-sector' command: LUN {lun}, Start LBA {startSector}, Sectors {sectorsToRead}, File '{filePath}'...")]
    internal static partial void ExecutingReadSector(
        this ILogger<ReadSectorCommand> logger,
        uint lun,
        ulong startSector,
        ulong sectorsToRead,
        string filePath);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Number of sectors to read must be greater than 0.")]
    internal static partial void SectorsCountMustBeGreaterThanZero(
        this ILogger<ReadSectorCommand> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Using storage type: {storageType}")]
    internal static partial void UsingStorageType(
        this ILogger<ReadSectorCommand> logger,
        StorageType storageType);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not get storage info for LUN {lun} (StorageType: {storageType}). Using default sector size.")]
    internal static partial void GetStorageInfoFailed(
        this ILogger<ReadSectorCommand> logger,
        uint lun,
        StorageType storageType,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Storage info unreliable or unavailable, using default sector size for {storageType}: {sectorSize}")]
    internal static partial void DefaultSectorSizeApplied(
        this ILogger<ReadSectorCommand> logger,
        StorageType storageType,
        uint sectorSize);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Using sector size: {sectorSize} bytes for LUN {lun}.")]
    internal static partial void UsingSectorSize(
        this ILogger<ReadSectorCommand> logger,
        uint sectorSize,
        uint lun);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message =
            "Sector range exceeds uint.MaxValue, which is not supported by the current Firehose.Read implementation.")]
    internal static partial void SectorRangeNotSupported(
        this ILogger<ReadSectorCommand> logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message =
            "Preparing to read {sectorsToRead} sectors (LBA {firstLba} to {lastLba}, {totalBytes} bytes) from LUN {lun} into '{filePath}'...")]
    internal static partial void PreparingReadSector(
        this ILogger<ReadSectorCommand> logger,
        ulong sectorsToRead,
        uint firstLba,
        uint lastLba,
        long totalBytes,
        uint lun,
        string filePath);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "IO error creating/writing to file '{filePath}'.")]
    internal static partial void IoErrorWritingFile(
        this ILogger<ReadSectorCommand> logger,
        string filePath,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to read sector data or write to stream.")]
    internal static partial void ReadSectorFailed(
        this ILogger<ReadSectorCommand> logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not delete partial file '{filePath}'.")]
    internal static partial void CouldNotDeletePartialFile(
        this ILogger<ReadSectorCommand> logger,
        string filePath,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Successfully read {mb:F2} MiB and wrote to '{filePath}' in {elapsed}.")]
    internal static partial void ReadSectorSucceeded(
        this ILogger<ReadSectorCommand> logger,
        double mb,
        string filePath,
        TimeSpan elapsed);
    
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "'read-sector' command finished in {seconds:F2}s.")]
    internal static partial void ReadSectorFinished(
        this ILogger<ReadSectorCommand> logger,
        double seconds);
}