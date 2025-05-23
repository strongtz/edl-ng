using Microsoft.Extensions.Logging;
using QCEDL.CLI.Commands;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace QCEDL.CLI.Logging;

internal static partial class Log
{
    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Executing 'write-sector' command: LUN {lun}, Start LBA {startSector}, File '{filePath}'...")]
    internal static partial void ExecutingWriteSector(
        this ILogger<WriteSectorCommand> logger,
        uint lun,
        ulong startSector,
        string filePath);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Input file '{filePath}' not found.")]
    internal static partial void InputFileNotFound(
        this ILogger<WriteSectorCommand> logger,
        string filePath);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Input file is empty. Nothing to write.")]
    internal static partial void InputFileEmpty(
        this ILogger<WriteSectorCommand> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Using storage type: {storageType}")]
    internal static partial void UsingStorageType(
        this ILogger<WriteSectorCommand> logger,
        StorageType storageType);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not get storage info for LUN {lun} (StorageType: {storageType}). Using default sector size.")]
    internal static partial void CouldNotGetStorageInfo(
        this ILogger<WriteSectorCommand> logger,
        uint lun,
        StorageType storageType,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Storage info unreliable or unavailable, using default sector size for {storageType}: {sectorSize}")]
    internal static partial void StorageInfoUnreliable(
        this ILogger<WriteSectorCommand> logger,
        StorageType storageType,
        uint sectorSize);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Using sector size: {sectorSize} bytes for LUN {lun}.")]
    internal static partial void UsingSectorSize(
        this ILogger<WriteSectorCommand> logger,
        uint sectorSize,
        uint lun);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message =
            "Input file size ({originalFileLength} bytes) is not a multiple of sector size ({sectorSize} bytes). Padding with zeros to {totalBytesToWriteIncludingPadding} bytes.")]
    internal static partial void InputFilePaddingWarning(
        this ILogger<WriteSectorCommand> logger,
        long originalFileLength,
        uint sectorSize,
        long totalBytesToWriteIncludingPadding);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message =
            "Data to write: {originalFileLength} bytes from file, padded to {totalBytesToWriteIncludingPadding} bytes ({numSectorsForXml} sectors).")]
    internal static partial void DataToWriteWithPadding(
        this ILogger<WriteSectorCommand> logger,
        long originalFileLength,
        long totalBytesToWriteIncludingPadding,
        uint numSectorsForXml);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message =
            "Start sector LBA ({startSector}) exceeds uint.MaxValue, which is not supported by the current Firehose.ProgramFromStream implementation's start_sector parameter.")]
    internal static partial void StartSectorLbaExceedsMaxValue(
        this ILogger<WriteSectorCommand> logger,
        ulong startSector);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message =
            "Attempting to write {numSectorsForXml} sectors ({totalBytesToWriteIncludingPadding} bytes) to LUN {lun}, starting at LBA {startSector}...")]
    internal static partial void AttemptingSectorWrite(
        this ILogger<WriteSectorCommand> logger,
        uint numSectorsForXml,
        long totalBytesToWriteIncludingPadding,
        uint lun,
        ulong startSector);


    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "IO Error reading input file '{filePath}'")]
    internal static partial void IoErrorReadingInputFile(
        this ILogger<WriteSectorCommand> logger,
        string filePath,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Data ({sizeMiB:F2} MiB) written to sectors successfully in {elapsed}.")]
    internal static partial void DataWrittenToSectors(
        this ILogger<WriteSectorCommand> logger,
        double sizeMiB,
        TimeSpan elapsed);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to write data to sectors. Check previous logs for NAK or errors.")]
    internal static partial void FailedToWriteToSectors(
        this ILogger<WriteSectorCommand> logger);
}