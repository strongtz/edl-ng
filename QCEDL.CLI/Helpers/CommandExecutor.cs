using System.Diagnostics;

namespace QCEDL.CLI.Helpers;

internal static class CommandExecutor
{
    public static async Task<int> RunAsync(string commandName, Func<Task<int>> action)
    {
        var commandStopwatch = Stopwatch.StartNew();
        try
        {
            return await action();
        }
        catch (FileNotFoundException ex)
        {
            Logging.Log(ex.Message, LogLevel.Error);
            return 1;
        }
        catch (ArgumentException ex)
        {
            Logging.Log(ex.Message, LogLevel.Error);
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            Logging.Log($"Operation Error: {ex.Message}", LogLevel.Error);
            return 1;
        }
        catch (IOException ex)
        {
            Logging.Log($"IO Error: {ex.Message}", LogLevel.Error);
            return 1;
        }
        catch (PlatformNotSupportedException ex)
        {
            Logging.Log($"Platform Error: {ex.Message}", LogLevel.Error);
            return 1;
        }
        catch (Exception ex)
        {
            Logging.Log($"An unexpected error occurred in '{commandName}': {ex.Message}", LogLevel.Error);
            Logging.Log(ex.ToString(), LogLevel.Debug);
            return 1;
        }
        finally
        {
            commandStopwatch.Stop();
            Logging.Log($"'{commandName}' command finished in {commandStopwatch.Elapsed.TotalSeconds:F2}s.", LogLevel.Debug);
        }
    }
}