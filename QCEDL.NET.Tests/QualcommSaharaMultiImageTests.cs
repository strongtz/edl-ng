using Qualcomm.EmergencyDownload.Layers.PBL.Sahara;
using Qualcomm.EmergencyDownload.Transport;

namespace QCEDL.NET.Tests;

public sealed class QualcommSaharaMultiImageTests : IDisposable
{
    private readonly string _configDirectory =
        Path.Combine(Path.GetTempPath(), $"qsahara-test-{Guid.NewGuid():N}");

    private readonly string _configPath;
    private readonly string _configWithoutProgrammerPath;

    public QualcommSaharaMultiImageTests()
    {
        _ = Directory.CreateDirectory(_configDirectory);
        WriteImage("sequencer_ram.elf", 0x200, 0x11);
        WriteImage("device_programmer_ddr.elf", 0x200, 0x22);
        _configPath = Path.Combine(_configDirectory, "qsahara_device_programmer.xml");
        File.WriteAllText(_configPath,
            """
            <?xml version="1.0" ?>
            <sahara_config>
            	<chipset>test</chipset>
            	<images>
            		<image image_id="59" image_path="sequencer_ram.elf"/>
            		<image image_id="13" image_path="device_programmer_ddr.elf"/>
            	</images>
            </sahara_config>
            """);
        _configWithoutProgrammerPath = Path.Combine(_configDirectory, "qsahara_no_programmer.xml");
        File.WriteAllText(_configWithoutProgrammerPath,
            """
            <?xml version="1.0" ?>
            <sahara_config>
            	<chipset>test</chipset>
            	<images>
            		<image image_id="59" image_path="sequencer_ram.elf"/>
            	</images>
            </sahara_config>
            """);
    }

    [Fact]
    public void EndsTheChainOnSilenceWhenTheConfigMapsNoProgrammerId()
    {
        using var transport = new ScriptedSaharaTransport(
        [
            ReadData64(59, 0, 0x40),
            EndImageTx(59, 0)
        ]);
        var sahara = new QualcommSahara(transport);

        Assert.True(sahara.SendImage(_configWithoutProgrammerPath));
        Assert.Equal(2, CountDonePackets(transport));
    }

    [Fact]
    public void SkipsUnexpectedSaharaCommandsInsteadOfFailingTheTransfer()
    {
        using var transport = new ScriptedSaharaTransport(
        [
            QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.CommandReady),
            QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.ResetResponse),
            ReadData32(13, 0, 0x20),
            EndImageTx(13, 0),
            DoneResponse(1)
        ]);
        var sahara = new QualcommSahara(transport);

        Assert.True(sahara.SendImage(_configPath));
    }

    [Fact]
    public void ServesWhateverImageIdTheTargetAsksForAndStopsOnImageTxComplete()
    {
        using var transport = new ScriptedSaharaTransport(
        [
            ReadData64(59, 0, 0x40),
            EndImageTx(59, 0),
            DoneResponse(0), // IMAGE_TX_PENDING -> another image follows
            Hello(2, 0),
            ReadData32(13, 0x10, 0x20),
            EndImageTx(13, 0),
            DoneResponse(1) // IMAGE_TX_COMPLETE -> leave Sahara
        ]);
        var sahara = new QualcommSahara(transport);

        Assert.True(sahara.SendImage(_configPath));

        var helloResponse = Assert.Single(transport.SentPackets,
            packet => (QualcommSaharaCommand)BitConverter.ToUInt32(packet, 0) ==
                      QualcommSaharaCommand.HelloResponse);
        Assert.Equal(0x30, helloResponse.Length);
        Assert.Equal(2u, BitConverter.ToUInt32(helloResponse, 0x08)); // version echoed
        Assert.Equal(2u, BitConverter.ToUInt32(helloResponse, 0x0C)); // version_supported echoed too
        Assert.Equal(0u, BitConverter.ToUInt32(helloResponse, 0x10)); // success status
        Assert.Equal(0u, BitConverter.ToUInt32(helloResponse, 0x14)); // mode echoed
        Assert.Equal([1u, 2u, 3u, 4u, 5u, 6u],
            Enumerable.Range(0, 6).Select(field => BitConverter.ToUInt32(helloResponse, 0x18 + (field * 4))));

        Assert.Equal(2, transport.SentPackets.Count(packet => packet.Length == 0x08 &&
            (QualcommSaharaCommand)BitConverter.ToUInt32(packet, 0) == QualcommSaharaCommand.Done));
        var rawWrites = transport.SentPackets.Where(packet => packet.Length is 0x40 or 0x20).ToList();
        Assert.Equal(2, rawWrites.Count);
        Assert.All(rawWrites[0], value => Assert.Equal(0x11, value));
        Assert.All(rawWrites[1], value => Assert.Equal(0x22, value));
    }

    [Fact]
    public void EndsTheChainOnTheDoneRespThatFollowsTheProgrammer()
    {
        using var transport = new ScriptedSaharaTransport(
        [
            ReadData32(13, 0, 0x20),
            EndImageTx(13, 0),
            DoneResponse(0)
        ]);
        var sahara = new QualcommSahara(transport);

        Assert.True(sahara.SendImage(_configPath));
        Assert.Equal(1, CountDonePackets(transport));
    }

    [Fact]
    public void TreatsSilenceAfterTheProgrammerAsTheEndOfTheChain()
    {
        using var transport = new ScriptedSaharaTransport(
        [
            ReadData32(13, 0, 0x20),
            EndImageTx(13, 0)
        ]);
        var sahara = new QualcommSahara(transport);

        Assert.True(sahara.SendImage(_configPath));
        Assert.Equal(2, CountDonePackets(transport));
    }

    private static int CountDonePackets(ScriptedSaharaTransport transport)
    {
        return transport.SentPackets.Count(packet => packet.Length == 0x08 &&
            (QualcommSaharaCommand)BitConverter.ToUInt32(packet, 0) == QualcommSaharaCommand.Done);
    }

    [Fact]
    public void FailsInsteadOfClaimingFirehoseWhenTheTargetGoesSilentMidChain()
    {
        using var transport = new ScriptedSaharaTransport(
        [
            ReadData64(59, 0, 0x40),
            EndImageTx(59, 0),
            DoneResponse(0) // IMAGE_TX_PENDING, then the target never speaks again
        ]);
        var sahara = new QualcommSahara(transport);

        Assert.False(sahara.SendImage(_configPath));
    }

    [Fact]
    public void ReattachesToTheTargetWhenItReenumeratesMidChain()
    {
        using var afterReenumeration = new ScriptedSaharaTransport(
        [
            Hello(2, 0),
            ReadData32(13, 0, 0x20),
            EndImageTx(13, 0),
            DoneResponse(1)
        ]);
        using var beforeReenumeration = new ScriptedSaharaTransport(
        [
            ReadData64(59, 0, 0x40),
            EndImageTx(59, 0),
            DoneResponse(0)
        ]);
        var sahara = new QualcommSahara(beforeReenumeration);
        var reconnects = 0;
        sahara.TransportReconnector = () =>
        {
            reconnects++;
            return afterReenumeration;
        };

        Assert.True(sahara.SendImage(_configPath));
        Assert.Equal(1, reconnects);
        Assert.Contains(afterReenumeration.SentPackets,
            packet => (QualcommSaharaCommand)BitConverter.ToUInt32(packet, 0) ==
                      QualcommSaharaCommand.Done);
    }

    [Fact]
    public void RejectsAnImageIdThatTheConfigDoesNotMap()
    {
        using var transport = new ScriptedSaharaTransport([ReadData64(99, 0, 0x40)]);
        var sahara = new QualcommSahara(transport);

        Assert.False(sahara.SendImage(_configPath));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_configDirectory, true);
        }
        catch (IOException)
        {
        }
    }

    private void WriteImage(string name, int length, byte fill)
    {
        var content = new byte[length];
        Array.Fill(content, fill);
        File.WriteAllBytes(Path.Combine(_configDirectory, name), content);
    }

    private static byte[] Hello(uint version, uint mode)
    {
        var payload = new byte[40];
        BitConverter.GetBytes(version).CopyTo(payload, 0x00);
        BitConverter.GetBytes(1u).CopyTo(payload, 0x04);
        BitConverter.GetBytes(0x400u).CopyTo(payload, 0x08);
        BitConverter.GetBytes(mode).CopyTo(payload, 0x0C);
        return QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.Hello, payload);
    }

    private static byte[] ReadData32(uint imageId, uint offset, uint length)
    {
        var payload = new byte[12];
        BitConverter.GetBytes(imageId).CopyTo(payload, 0x00);
        BitConverter.GetBytes(offset).CopyTo(payload, 0x04);
        BitConverter.GetBytes(length).CopyTo(payload, 0x08);
        return QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.ReadData, payload);
    }

    private static byte[] ReadData64(ulong imageId, ulong offset, ulong length)
    {
        var payload = new byte[24];
        BitConverter.GetBytes(imageId).CopyTo(payload, 0x00);
        BitConverter.GetBytes(offset).CopyTo(payload, 0x08);
        BitConverter.GetBytes(length).CopyTo(payload, 0x10);
        return QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.ReadData64Bit, payload);
    }

    private static byte[] EndImageTx(uint imageId, uint status)
    {
        var payload = new byte[8];
        BitConverter.GetBytes(imageId).CopyTo(payload, 0x00);
        BitConverter.GetBytes(status).CopyTo(payload, 0x04);
        return QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.EndImageTx, payload);
    }

    private static byte[] DoneResponse(uint imageTxStatus)
    {
        return QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.DoneResponse,
            BitConverter.GetBytes(imageTxStatus));
    }
}

internal sealed class ScriptedSaharaTransport(
    IEnumerable<byte[]> packets,
    TransportBackend backend = TransportBackend.LibUsb) : IQualcommTransport
{
    private readonly Queue<byte[]> _packets = new(packets);

    internal List<byte[]> SentPackets { get; } = [];

    public TransportBackend Backend => backend;

    public int TimeoutMilliseconds { get; set; } = 1000;

    public int Read(byte[] buffer, int offset, int count)
    {
        if (_packets.Count == 0)
        {
            throw new TimeoutException("Scripted transport has no more packets.");
        }

        var packet = _packets.Dequeue();
        Assert.True(packet.Length <= count);
        Buffer.BlockCopy(packet, 0, buffer, offset, packet.Length);
        return packet.Length;
    }

    public int Write(byte[] buffer, int offset, int count)
    {
        SentPackets.Add(buffer.AsSpan(offset, count).ToArray());
        return count;
    }

    public void SendZeroLengthPacket()
    {
    }

    public void Dispose()
    {
    }
}
