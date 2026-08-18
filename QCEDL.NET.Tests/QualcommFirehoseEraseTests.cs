using System.Text;
using System.Xml.Linq;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;
using Qualcomm.EmergencyDownload.Transport;

namespace QCEDL.NET.Tests;

public sealed class QualcommFirehoseEraseTests
{
    [Fact]
    public void ErasePhysicalPartitionOmitsSectorRange()
    {
        using var transport = new FirehoseEraseRecordingTransport(Ack);
        var firehose = new QualcommFirehose(transport);

        var success = firehose.ErasePhysicalPartition(StorageType.Ufs, 2, 1, 4096);

        Assert.True(success);
        var erase = GetEraseElement(Assert.Single(transport.Commands));
        Assert.Equal("UFS", erase.Attribute("storage_type")?.Value);
        Assert.Equal("2", erase.Attribute("physical_partition_number")?.Value);
        Assert.Equal("1", erase.Attribute("slot")?.Value);
        Assert.Equal("4096", erase.Attribute("SECTOR_SIZE_IN_BYTES")?.Value);
        Assert.Null(erase.Attribute("start_sector"));
        Assert.Null(erase.Attribute("num_partition_sectors"));
    }

    [Fact]
    public void RangedEraseRetainsSectorRange()
    {
        using var transport = new FirehoseEraseRecordingTransport(Ack);
        var firehose = new QualcommFirehose(transport);

        var success = firehose.Erase(StorageType.Ufs, 2, 1, 4096, 10, 20);

        Assert.True(success);
        var erase = GetEraseElement(Assert.Single(transport.Commands));
        Assert.Equal("10", erase.Attribute("start_sector")?.Value);
        Assert.Equal("20", erase.Attribute("num_partition_sectors")?.Value);
    }

    [Fact]
    public void EraseAllUsesReportedPhysicalPartitionCount()
    {
        using var transport = new FirehoseEraseRecordingTransport(Ack, Ack, Ack);
        var firehose = new QualcommFirehose(transport);

        var success = firehose.EraseAll(StorageType.Ufs, 0, 4096, 3);

        Assert.True(success);
        Assert.Equal(FirstThreePhysicalPartitions, GetPhysicalPartitionNumbers(transport));
    }

    [Fact]
    public void EraseAllFailsWhenReportedPhysicalPartitionCannotBeErased()
    {
        using var transport = new FirehoseEraseRecordingTransport(Ack, Nak, Ack);
        var firehose = new QualcommFirehose(transport);

        var success = firehose.EraseAll(StorageType.Ufs, 0, 4096, 3);

        Assert.False(success);
        Assert.Equal(FirstTwoPhysicalPartitions, GetPhysicalPartitionNumbers(transport));
    }

    [Fact]
    public void EraseAllFallbackStopsSuccessfullyAtFirstUnavailablePartitionAfterZero()
    {
        using var transport = new FirehoseEraseRecordingTransport(Ack, Ack, Nak, Ack);
        var firehose = new QualcommFirehose(transport);

        var success = firehose.EraseAll(StorageType.Ufs, 0, 4096, null);

        Assert.True(success);
        Assert.Equal(FirstThreePhysicalPartitions, GetPhysicalPartitionNumbers(transport));
    }

    [Fact]
    public void EraseAllFallbackFailsWhenPhysicalPartitionZeroCannotBeErased()
    {
        using var transport = new FirehoseEraseRecordingTransport(Nak, Ack);
        var firehose = new QualcommFirehose(transport);

        var success = firehose.EraseAll(StorageType.Ufs, 0, 4096, null);

        Assert.False(success);
        Assert.Equal(PhysicalPartitionZero, GetPhysicalPartitionNumbers(transport));
    }

    [Fact]
    public void EraseAllFallbackProbesAtMostEightPhysicalPartitions()
    {
        using var transport = new FirehoseEraseRecordingTransport([.. Enumerable.Repeat(Ack, 8)]);
        var firehose = new QualcommFirehose(transport);

        var success = firehose.EraseAll(StorageType.Ufs, 0, 4096, null);

        Assert.True(success);
        Assert.Equal(FirstEightPhysicalPartitions, GetPhysicalPartitionNumbers(transport));
    }

    private static readonly byte[] Ack = "<data><response value=\"ACK\" /></data>"u8.ToArray();
    private static readonly byte[] Nak = "<data><response value=\"NAK\" /></data>"u8.ToArray();
    private static readonly string[] FirstThreePhysicalPartitions = ["0", "1", "2"];
    private static readonly string[] FirstTwoPhysicalPartitions = ["0", "1"];
    private static readonly string[] PhysicalPartitionZero = ["0"];
    private static readonly string[] FirstEightPhysicalPartitions = ["0", "1", "2", "3", "4", "5", "6", "7"];

    private static XElement GetEraseElement(byte[] command)
    {
        return XDocument.Parse(Encoding.UTF8.GetString(command)).Root?.Element("erase")
               ?? throw new InvalidDataException("Erase element was not present in command XML.");
    }

    private static string[] GetPhysicalPartitionNumbers(FirehoseEraseRecordingTransport transport)
    {
        return
        [
            .. transport.Commands.Select(command =>
                GetEraseElement(command).Attribute("physical_partition_number")?.Value ?? "")
        ];
    }
}

internal sealed class FirehoseEraseRecordingTransport(params byte[][] responses) : IQualcommTransport
{
    private readonly Queue<byte[]> _responses = new(responses);

    internal List<byte[]> Commands { get; } = [];

    public TransportBackend Backend => TransportBackend.WindowsQud;
    public int TimeoutMilliseconds { get; set; } = 1000;

    public int Read(byte[] buffer, int offset, int count)
    {
        if (!_responses.TryDequeue(out var response))
        {
            throw new TimeoutException("No queued Firehose response.");
        }

        Buffer.BlockCopy(response, 0, buffer, offset, response.Length);
        return response.Length;
    }

    public int Write(byte[] buffer, int offset, int count)
    {
        Commands.Add(buffer.AsSpan(offset, count).ToArray());
        return count;
    }

    public void SendZeroLengthPacket()
    {
    }

    public void Dispose()
    {
    }
}
