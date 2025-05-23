using Microsoft.Extensions.Logging;

namespace QCEDL.CLI.Logging;

internal static partial class Log
{
    [LoggerMessage(
        Level = LogLevel.Error)]
    internal static partial void ExceptedException(
        this ILogger logger,
        Exception ex);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "An unexpected error occurred.")]
    internal static partial void UnexceptedException(
        this ILogger logger,
        Exception ex);
}