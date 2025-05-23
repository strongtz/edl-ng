using Microsoft.Extensions.Logging;
using QCEDL.CLI.Commands;
using QCEDL.CLI.Core;

namespace QCEDL.CLI.Logging;

internal static partial class Log
{
    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Executing 'upload-loader' command...")]
    internal static partial void ExecutingUploadLoader(
        this ILogger<UploadLoaderCommand> logger);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The '--loader' option is required for the 'upload-loader' command.")]
    internal static partial void LoaderOptionMissing(
        this ILogger<UploadLoaderCommand> logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Device detected in Sahara mode. Proceeding with loader upload...")]
    internal static partial void SaharaModeDetected(
        this ILogger<UploadLoaderCommand> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Loader upload process completed. Device should restart or re-enumerate.")]
    internal static partial void LoaderUploadCompleted(
        this ILogger<UploadLoaderCommand> logger);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Device is already in Firehose mode. Cannot upload loader.")]
    internal static partial void AlreadyInFirehose(
        this ILogger<UploadLoaderCommand> logger);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Cannot upload loader. Device mode is {deviceMode} or could not be reliably determined.")]
    internal static partial void CannotUploadLoaderUnknownMode(
        this ILogger<UploadLoaderCommand> logger,
        DeviceMode deviceMode);
}