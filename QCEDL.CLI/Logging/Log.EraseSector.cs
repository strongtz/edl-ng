using Microsoft.Extensions.Logging;
using QCEDL.CLI.Commands;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace QCEDL.CLI.Logging;

internal static partial class Log
{
    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Executing 'erase-sector' command: LUN {lun}, Start LBA {startLba}, Sectors {sectors}...")]
    internal static partial void ExecutingEraseSectorCommand(
        this ILogger<EraseSectorCommand> logger,
        uint lun,
        ulong startLba,
        ulong sectors);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Number of sectors to erase must be greater than 0.")]
    internal static partial void InvalidSectorCount(
        this ILogger<EraseSectorCommand> logger);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Sector range (Start: {startLba}, Count: {sectors}) exceeds uint.MaxValue, which is not supported by the current Firehose.Erase implementation.")]
    internal static partial void SectorRangeExceedsUintMax(
        this ILogger<EraseSectorCommand> logger,
        ulong startLba,
        ulong sectors);
  [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Using storage type: {storageType}")]
    internal static partial void UsingStorageType(
        this ILogger<EraseSectorCommand> logger,
        StorageType storageType);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not get storage info for LUN {lun} (StorageType: {storageType}). Using default sector size.")]
    internal static partial void StorageInfoUnavailable(
        this ILogger<EraseSectorCommand> logger,
        uint lun,
        StorageType storageType,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Storage info unreliable or unavailable, using default sector size for {storageType}: {sectorSize}")]
    internal static partial void DefaultSectorSizeWarning(
        this ILogger<EraseSectorCommand> logger,
        StorageType storageType,
        uint sectorSize);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Using sector size: {sectorSize} bytes for LUN {lun}.")]
    internal static partial void UsingSectorSize(
        this ILogger<EraseSectorCommand> logger,
        uint sectorSize,
        uint lun);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Attempting to erase {numSectors} sectors starting at LBA {startSector} on LUN {lun}...")]
    internal static partial void AttemptEraseSectors(
        this ILogger<EraseSectorCommand> logger,
        ulong numSectors,
        ulong startSector,
        uint lun);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Successfully erased {numSectors} sectors in {elapsed}.")]
    internal static partial void EraseSectorsSucceeded(
        this ILogger<EraseSectorCommand> logger,
        ulong numSectors,
        TimeSpan elapsed);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to erase sectors. Check previous logs for NAK or errors.")]
    internal static partial void EraseSectorsFailed(
        this ILogger<EraseSectorCommand> logger);
    
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "'erase-sector' command finished in {elapsed}.")]
    internal static partial void EraseSectorCommandFinished(
        this ILogger<EraseSectorCommand> logger,
        TimeSpan elapsed);
}