using ExhaustiveMatching;
using H.Formatters;
using H.Pipes;
using H.Pipes.Args;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QCEDL.GUI.Helper.Messages;

namespace QCEDL.GUI.Helper;

internal sealed partial class HelperHostService(
    ILogger<HelperHostService> logger,
    IHostApplicationLifetime applicationLifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            Umask.Set(~Convert.ToUInt32("666", 8));
        }

        await using var server =
            new PipeServer<IMessage>(
                Pipe.Name,
                new SystemTextJsonFormatter());

        LogStaringHelperService();

        server.ClientConnected += HandleClientConnected;

        server.MessageReceived += HandleServerMessageReceived;

        await server.StartAsync(stoppingToken);

        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    private async void HandleServerMessageReceived(object? sender, ConnectionMessageEventArgs<IMessage?> e)
    {
        try
        {
            if (e.Message is not IClientMessage clientMessage)
            {
                LogUnexpectedMessage(e.Message);
                return;
            }

            LogClientMessage(clientMessage);

            switch (clientMessage)
            {
                case ClientHello hello:
                    await e.Connection.WriteAsync(new ServerHello(hello.Hello));
                    break;
                case ClientTerminate _:
                    applicationLifetime.StopApplication();
                    break;
                default:
                    throw ExhaustiveMatch.Failed(clientMessage);
            }
        }
        catch (Exception ex)
        {
            LogUnexceptedException(ex);
        }
    }

    private async void HandleClientConnected(object? sender, ConnectionEventArgs<IMessage> e)
    {
        try
        {
            LogClientConnection(e.Connection.PipeName);

            await e.Connection.WriteAsync(new ServerHello("Hello from server"), CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogUnexceptedException(ex);
        }
    }

    [LoggerMessage(
        LogLevel.Information,
        "Starting helper service.")]
    private partial void LogStaringHelperService();

    [LoggerMessage(
        LogLevel.Information,
        "Client connected through {pipeName}")]
    private partial void LogClientConnection(string pipeName);

    [LoggerMessage(
        LogLevel.Information,
        "Received client message: {message}")]
    private partial void LogClientMessage(IClientMessage message);

    [LoggerMessage(
        LogLevel.Error,
        "Excepted client message, but got: {message}")]
    private partial void LogUnexpectedMessage(IMessage? message);

    [LoggerMessage(
        LogLevel.Error,
        "Unexcepted exception occured.")]
    private partial void LogUnexceptedException(Exception exception);
}