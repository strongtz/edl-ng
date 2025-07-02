using System.Threading.Channels;
using H.Formatters;
using H.Pipes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QCEDL.GUI.Helper.Messages;

namespace QCEDL.GUI.Helper;

public abstract partial class HelperLauncherBase(ILogger logger) : IHostedService, IHelperLauncher, IAsyncDisposable
{
    private readonly Channel<IClientMessage> _senderChannel = Channel.CreateUnbounded<IClientMessage>();

    private readonly Channel<IServerMessage> _receiverChannel = Channel.CreateUnbounded<IServerMessage>();

    private readonly CancellationTokenSource _cancellationTokenSource = new();

    private bool _disposed;

    private Task? _launchingTask;

    private PipeClient<IMessage>? _client;

    public ChannelWriter<IClientMessage> Sender => _senderChannel.Writer;

    public ChannelReader<IServerMessage> Receiver => _receiverChannel.Reader;

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_launchingTask is { IsCompleted: true })
        {
            await CastAndDispose(_launchingTask);
        }

        await CastAndDispose(_cancellationTokenSource);

        if (_client != null)
        {
            await _client.DisposeAsync();
        }

        return;

        static async ValueTask CastAndDispose(IDisposable resource)
        {
            if (resource is IAsyncDisposable resourceAsyncDisposable)
            {
                await resourceAsyncDisposable.DisposeAsync();
            }
            else
            {
                resource.Dispose();
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _launchingTask = ExecuteAsync(_cancellationTokenSource.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        LogTerminating();

        if (_client is null || !_client.IsConnected)
        {
            return;
        }

        await _client.WriteAsync(new ClientTerminate(), CancellationToken.None);
        await _cancellationTokenSource.CancelAsync().ConfigureAwait(false);

        await DisposeAsync();
    }

    protected abstract void LaunchHelperProcess();

    private async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LaunchHelperProcess();
        _client = new(
            Pipe.Name,
            new SystemTextJsonFormatter());

        _client.MessageReceived += async (sender, e) =>
        {
            if (e.Message is not IServerMessage serverMessage)
            {
                return;
            }

            LogEventReceived(serverMessage);
            await _receiverChannel.Writer.WriteAsync(serverMessage, stoppingToken);
        };

        await _client.ConnectAsync(stoppingToken);

        await _client.WriteAsync(new ClientHello("Hello from GUI"), stoppingToken);

        await foreach (var clientMessage in _senderChannel.Reader.ReadAllAsync(stoppingToken))
        {
            await _client.WriteAsync(clientMessage, stoppingToken);
        }
    }

    [LoggerMessage(
        LogLevel.Information,
        "Terminating helper process")]
    private partial void LogTerminating();

    [LoggerMessage(
        LogLevel.Information,
        "Received server message: {message}")]
    private partial void LogEventReceived(IServerMessage message);
}