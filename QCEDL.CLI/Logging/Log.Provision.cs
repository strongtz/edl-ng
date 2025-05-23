using Microsoft.Extensions.Logging;
using QCEDL.CLI.Commands;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace QCEDL.CLI.Logging;

internal static partial class Log
{
    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Executing 'provision' command with XML file: {xmlFile}")]
    internal static partial void ExecutingProvisionCommand(
        this ILogger<ProvisionCommand> logger,
        string xmlFile);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message =
            "--memory is set to '{memoryType}'. UFS provisioning command implies UFS. Using UFS for this operation.")]
    internal static partial void MemoryOptionWarning(
        this ILogger<ProvisionCommand> logger,
        StorageType? memoryType);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Sending initial Firehose configure command (Memory: UFS, SkipStorageInit: true)...")]
    internal static partial void SendingInitialConfigure(
        this ILogger<ProvisionCommand> logger);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to send initial Firehose configure command for provisioning.")]
    internal static partial void FailedInitialConfigure(
        this ILogger<ProvisionCommand> logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Initial Firehose configure command sent successfully.")]
    internal static partial void InitialConfigureSuccess(
        this ILogger<ProvisionCommand> logger);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error parsing XML file '{xmlFile}'.")]
    internal static partial void ErrorParsingXml(
        this ILogger<ProvisionCommand> logger,
        string xmlFile,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Invalid XML structure: Root element must be <data>.")]
    internal static partial void InvalidXmlStructure(
        this ILogger<ProvisionCommand> logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "No <ufs> elements found in the XML file.")]
    internal static partial void NoUfsElements(
        this ILogger<ProvisionCommand> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Found {count} <ufs> elements to process.")]
    internal static partial void FoundUfsElements(
        this ILogger<ProvisionCommand> logger,
        int count);
    
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Sending UFS command {current}/{total}")]
    internal static partial void SendingUfsCommand(
        this ILogger<ProvisionCommand> logger,
        int current,
        int total);
    
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "UFS command {index} ACKed.")]
    internal static partial void UfsCommandAcked(
        this ILogger<ProvisionCommand> logger,
        int index);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to send UFS command {index} or received NAK: {ufsElementString}")]
    internal static partial void FailedUfsCommand(
        this ILogger<ProvisionCommand> logger,
        int index,
        string ufsElementString);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Aborting provisioning. {successCount}/{totalCount} commands succeeded before failure.")]
    internal static partial void AbortingProvisioning(
        this ILogger<ProvisionCommand> logger,
        int successCount,
        int totalCount);
    
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "UFS provisioning completed. All {successCount}/{totalCount} commands sent and ACKed successfully.")]
    internal static partial void UfsProvisioningCompleted(
        this ILogger<ProvisionCommand> logger,
        int successCount,
        int totalCount);
}