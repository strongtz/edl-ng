using Microsoft.Extensions.Logging;
using QCEDL.CLI.Commands;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace QCEDL.CLI.Logging;

internal static partial class Log
{
    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Executing 'reset' command: Mode '{mode}', Delay '{delay}s'...")]
    internal static partial void ExecutingReset(
        this ILogger<ResetCommand> logger,
        PowerValue mode,
        uint delay);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Attempting to send power command: Mode '{mode}', Delay '{delay}s'...")]
    internal static partial void AttemptingPowerCommand(
        this ILogger<ResetCommand> logger,
        PowerValue mode,
        uint delay);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Power command '{mode}' sent successfully.")]
    internal static partial void PowerCommandSucceeded(
        this ILogger<ResetCommand> logger,
        PowerValue mode);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Device should now be resetting.")]
    internal static partial void DeviceResetting(
        this ILogger<ResetCommand> logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Device should now be powering off.")]
    internal static partial void DevicePoweringOff(
        this ILogger<ResetCommand> logger);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to send power command '{mode}'. Check previous logs for NAK or errors.")]
    internal static partial void PowerCommandFailed(
        this ILogger<ResetCommand> logger,
        PowerValue mode);
}