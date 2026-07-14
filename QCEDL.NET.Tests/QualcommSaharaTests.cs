using Qualcomm.EmergencyDownload.Layers.PBL.Sahara;
using Qualcomm.EmergencyDownload.Transport;

namespace QCEDL.NET.Tests;

public sealed class QualcommSaharaTests
{
    [Fact]
    public void ProbeCommandModeUsesPreReadHello()
    {
        var responses = new Queue<byte[]>(
        [
            QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.CommandReady)
        ]);
        using var transport = new SaharaRecordingTransport(responses);
        var sahara = new QualcommSahara(transport);
        var hello = QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.Hello, new byte[40]);
        BitConverter.GetBytes(3u).CopyTo(hello, 0x08);

        var result = sahara.ProbeCommandMode(hello);

        Assert.Equal(QualcommSaharaHandshakeResult.Sahara, result);
        Assert.Equal(3u, sahara.DetectedDeviceSaharaVersion);
        Assert.Equal([QualcommSaharaCommand.HelloResponse], transport.SentCommands);
    }

    [Fact]
    public void ProbeCommandModeRecoversWhenWindowsQudDiscardsHello()
    {
        var responses = new Queue<byte[]>(
        [
            QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.CommandReady)
        ]);
        using var transport = new SaharaRecordingTransport(responses, initialReadTimeouts: 1);
        var sahara = new QualcommSahara(transport);

        var result = sahara.ProbeCommandMode();

        Assert.Equal(QualcommSaharaHandshakeResult.Sahara, result);
        var helloResponse = Assert.Single(transport.SentPackets);
        Assert.Equal(QualcommSaharaCommand.HelloResponse, ReadCommand(helloResponse));
        Assert.Equal(QualcommSaharaMode.Command, (QualcommSaharaMode)BitConverter.ToUInt32(helloResponse, 0x14));
    }

    [Fact]
    public void ProbeCommandModeRecognizesFirehoseAfterSpeculativeHelloResponse()
    {
        var responses = new Queue<byte[]>(
        [
            "<?xml version=\"1.0\" ?>"u8.ToArray()
        ]);
        using var transport = new SaharaRecordingTransport(responses, initialReadTimeouts: 1);
        var sahara = new QualcommSahara(transport);

        var result = sahara.ProbeCommandMode();

        Assert.Equal(QualcommSaharaHandshakeResult.Firehose, result);
        _ = Assert.Single(transport.SentPackets);
    }

    [Fact]
    public void ProbeCommandModeCanRecoverAnAlreadyTimedOutInitialRead()
    {
        var responses = new Queue<byte[]>(
        [
            QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.CommandReady)
        ]);
        using var transport = new SaharaRecordingTransport(responses);
        var sahara = new QualcommSahara(transport);

        var result = sahara.ProbeCommandMode(initialReadAlreadyTimedOut: true);

        Assert.Equal(QualcommSaharaHandshakeResult.Sahara, result);
        _ = Assert.Single(transport.SentPackets);
        Assert.Empty(responses);
    }

    [Fact]
    public void ProbeCommandModeDoesNotSpeculateOnLibUsb()
    {
        using var transport = new SaharaRecordingTransport(
            new Queue<byte[]>(),
            TransportBackend.LibUsb,
            initialReadTimeouts: 1);
        var sahara = new QualcommSahara(transport);

        var result = sahara.ProbeCommandMode();

        Assert.Equal(QualcommSaharaHandshakeResult.Failed, result);
        Assert.Empty(transport.SentPackets);
    }

    [Fact]
    public void SendImageRecoversWhenWindowsQudDiscardsHello()
    {
        var responses = new Queue<byte[]>(
        [
            QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.EndImageTx, new byte[8]),
            QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.DoneResponse, new byte[4])
        ]);
        using var transport = new SaharaRecordingTransport(responses, initialReadTimeouts: 1);
        var sahara = new QualcommSahara(transport);
        var programmerPath = Path.GetTempFileName();

        try
        {
            var succeeded = sahara.SendImage(programmerPath);

            Assert.True(succeeded);
            Assert.Equal(
                [QualcommSaharaCommand.HelloResponse, QualcommSaharaCommand.Done],
                transport.SentCommands);
            Assert.Equal(
                QualcommSaharaMode.ImageTxPending,
                (QualcommSaharaMode)BitConverter.ToUInt32(transport.SentPackets[0], 0x14));
        }
        finally
        {
            File.Delete(programmerPath);
        }
    }

    [Fact]
    public void SendImageRecognizesFirehoseAfterSpeculativeHelloResponse()
    {
        var responses = new Queue<byte[]>(
        [
            "<?xml version=\"1.0\" ?>"u8.ToArray()
        ]);
        using var transport = new SaharaRecordingTransport(responses, initialReadTimeouts: 1);
        var sahara = new QualcommSahara(transport);
        var programmerPath = Path.GetTempFileName();

        try
        {
            var succeeded = sahara.SendImage(programmerPath);

            Assert.True(succeeded);
            Assert.Equal([QualcommSaharaCommand.HelloResponse], transport.SentCommands);
        }
        finally
        {
            File.Delete(programmerPath);
        }
    }

    [Fact]
    public async Task LoadProgrammerSendsDoneExactlyOnce()
    {
        var responses = new Queue<byte[]>(
        [
            QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.Hello, new byte[40]),
            QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.EndImageTx, new byte[8]),
            QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.DoneResponse, new byte[4])
        ]);
        using var transport = new SaharaRecordingTransport(responses);
        var sahara = new QualcommSahara(transport);
        var programmerPath = Path.GetTempFileName();

        try
        {
            var succeeded = await sahara.LoadProgrammer(programmerPath);

            Assert.True(succeeded);
            Assert.Equal(
                [QualcommSaharaCommand.HelloResponse, QualcommSaharaCommand.Done],
                transport.SentCommands);
            Assert.Empty(responses);
        }
        finally
        {
            File.Delete(programmerPath);
        }
    }

    private static QualcommSaharaCommand ReadCommand(byte[] packet)
    {
        return (QualcommSaharaCommand)BitConverter.ToUInt32(packet, 0);
    }
}

internal sealed class SaharaRecordingTransport(
    Queue<byte[]> responses,
    TransportBackend backend = TransportBackend.WindowsQud,
    int initialReadTimeouts = 0) : IQualcommTransport
{
    internal List<QualcommSaharaCommand> SentCommands { get; } = [];
    internal List<byte[]> SentPackets { get; } = [];

    private int _remainingReadTimeouts = initialReadTimeouts;

    public TransportBackend Backend => backend;
    public int TimeoutMilliseconds { get; set; } = 1000;

    public int Read(byte[] buffer, int offset, int count)
    {
        if (_remainingReadTimeouts > 0)
        {
            _remainingReadTimeouts--;
            throw new TimeoutException("Simulated read timeout.");
        }

        var response = responses.Dequeue();
        Assert.True(response.Length <= count);
        Buffer.BlockCopy(response, 0, buffer, offset, response.Length);
        return response.Length;
    }

    public int Write(byte[] buffer, int offset, int count)
    {
        Assert.True(count >= sizeof(uint));
        var packet = buffer.AsSpan(offset, count).ToArray();
        SentPackets.Add(packet);
        SentCommands.Add((QualcommSaharaCommand)BitConverter.ToUInt32(packet, 0));
        return count;
    }

    public void SendZeroLengthPacket()
    {
    }

    public void Dispose()
    {
    }
}