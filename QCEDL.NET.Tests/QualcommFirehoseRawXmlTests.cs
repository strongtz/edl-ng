using System.Diagnostics;
using System.Text;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Transport;

namespace QCEDL.NET.Tests;

public sealed class QualcommFirehoseRawXmlTests
{
    [Theory]
    [InlineData("ACK", true)]
    [InlineData("NAK", false)]
    public void WaitsThroughManyLogsForResponse(string response, bool expected)
    {
        using var transport = new FirehoseEraseRecordingTransport(
            [.. Enumerable.Repeat(Log, 30), Encoding.UTF8.GetBytes($"<data><response value=\"{response}\" /></data>")]);
        var firehose = new QualcommFirehose(transport);

        Assert.Equal(expected, firehose.SendRawXmlAndGetResponse(Command));
        _ = Assert.Single(transport.Commands);
        Assert.Equal(1000, transport.TimeoutMilliseconds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeadlineExpiresEvenWithContinuousLogs(bool emitLogs)
    {
        using var transport = new WaitingTransport(emitLogs);
        var firehose = new QualcommFirehose(transport);
        var timer = Stopwatch.StartNew();

        Assert.False(firehose.SendRawXmlAndGetResponse(Command, 50));
        Assert.InRange(timer.ElapsedMilliseconds, 40, 5000);
        Assert.Equal(1, transport.Writes);
        Assert.Equal(1000, transport.TimeoutMilliseconds);
        Assert.InRange(transport.LargestReadTimeout, 1, 50);
    }

    [Fact]
    public void RestoresTimeoutOnTransportFailure()
    {
        using var transport = new WaitingTransport(false) { Fail = true };
        var firehose = new QualcommFirehose(transport);
        _ = Assert.Throws<IOException>(() => firehose.SendRawXmlAndGetResponse(Command, 120000));
        Assert.Equal(1000, transport.TimeoutMilliseconds);
        Assert.InRange(transport.LargestReadTimeout, 110000, 120000);
    }

    private const string Command = "<data><ufs LUNtoGrow=\"0\" commit=\"1\" /></data>";
    private static readonly byte[] Log = "<data><log value=\"INFO: processing ufs\" /></data>"u8.ToArray();

    private sealed class WaitingTransport(bool emitLogs) : IQualcommTransport
    {
        public TransportBackend Backend => TransportBackend.WindowsQud;
        public int TimeoutMilliseconds { get; set; } = 1000;
        public int Writes { get; private set; }
        public int LargestReadTimeout { get; private set; }
        public bool Fail { get; init; }

        public int Read(byte[] buffer, int offset, int count)
        {
            LargestReadTimeout = Math.Max(LargestReadTimeout, TimeoutMilliseconds);
            if (Fail)
            {
                throw new IOException("Disconnected");
            }
            if (!emitLogs)
            {
                throw new TimeoutException("No response yet");
            }
            Log.CopyTo(buffer, offset);
            return Log.Length;
        }

        public int Write(byte[] buffer, int offset, int count)
        {
            Writes++;
            return count;
        }

        public void SendZeroLengthPacket() { }
        public void Dispose() { }
    }
}
