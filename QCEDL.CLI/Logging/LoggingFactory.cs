using Microsoft.Extensions.Logging;

namespace QCEDL.CLI.Logging;

public static class LoggingFactory
{
    private static ILoggerFactory? _loggerFactory;

    public static void ConfigureLogging(Action<ILoggingBuilder> configureLogging)
    {
        if (_loggerFactory is not null)
        {
            throw new InvalidOperationException("Logging has already been configured.");
        }

        _loggerFactory = LoggerFactory.Create(configureLogging);
    }

    public static ILogger<T> GetLogger<T>()
    {
        if (_loggerFactory is null)
        {
            throw new InvalidOperationException("Logging has not been configured.");
        }

        return _loggerFactory.CreateLogger<T>();
    }
}