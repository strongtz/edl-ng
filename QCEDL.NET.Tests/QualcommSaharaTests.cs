using Qualcomm.EmergencyDownload.Layers.PBL.Sahara;
using Qualcomm.EmergencyDownload.Transport;

namespace QCEDL.NET.Tests;

public sealed class QualcommSaharaTests
{
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
}

internal sealed class SaharaRecordingTransport(Queue<byte[]> responses) : IQualcommTransport
{
    internal List<QualcommSaharaCommand> SentCommands { get; } = [];

    public TransportBackend Backend => TransportBackend.WindowsQud;
    public int TimeoutMilliseconds { get; set; } = 1000;

    public int Read(byte[] buffer, int offset, int count)
    {
        var response = responses.Dequeue();
        Assert.True(response.Length <= count);
        Buffer.BlockCopy(response, 0, buffer, offset, response.Length);
        return response.Length;
    }

    public int Write(byte[] buffer, int offset, int count)
    {
        Assert.True(count >= sizeof(uint));
        SentCommands.Add((QualcommSaharaCommand)BitConverter.ToUInt32(buffer, offset));
        return count;
    }

    public void SendZeroLengthPacket()
    {
    }

    public void Dispose()
    {
    }
}