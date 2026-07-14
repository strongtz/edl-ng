using System.Buffers.Binary;
using System.Text;
using Qualcomm.EmergencyDownload.Layers.PBL.Sahara;
using Qualcomm.EmergencyDownload.Transport;

namespace QCEDL.NET.Tests;

public sealed class QualcommSaharaRamDumpTests
{
    private const int DebugBlockSize = 1024 * 1024;

    [Fact]
    public void CollectRamDumpCompletesHandshakeWritesFileAndResets()
    {
        var table = BuildTable(new RamDumpRegionDefinition(7, 0x2000, 6, "OCIMEM", "OCIMEM.BIN"));
        using var transport = new RamDumpRecordingTransport(
            TransportBackend.WindowsQud,
            BuildHello(QualcommSaharaMode.MemoryDebug),
            BuildMemoryDebug64(0x1000, (ulong)table.Length),
            table,
            new byte[] { 1, 2 },
            new byte[] { 3, 4, 5, 6 },
            BuildSimplePacket(QualcommSaharaCommand.ResetResponse));
        using var output = new TemporaryDirectory();
        var progress = new List<QualcommSaharaRamDumpProgress>();

        var regions = new QualcommSahara(transport).CollectRamDump(
            output.DirectoryPath,
            progress: progress.Add);

        var region = Assert.Single(regions);
        Assert.Equal(7UL, region.Type);
        Assert.Equal(0x2000UL, region.Address);
        Assert.Equal(6UL, region.Length);
        Assert.Equal("OCIMEM", region.RegionName);
        Assert.Equal("OCIMEM.BIN", region.FileName);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, File.ReadAllBytes(output.GetPath("OCIMEM.BIN")));
        Assert.False(File.Exists(output.GetPath("OCIMEM.BIN.partial")));

        Assert.Equal(
            [
                QualcommSaharaCommand.HelloResponse,
                QualcommSaharaCommand.MemoryRead64Bit,
                QualcommSaharaCommand.MemoryRead64Bit,
                QualcommSaharaCommand.Reset
            ],
            transport.SentCommands);
        Assert.Equal(QualcommSaharaMode.MemoryDebug,
            (QualcommSaharaMode)BinaryPrimitives.ReadUInt32LittleEndian(transport.SentPackets[0].AsSpan(0x14)));
        Assert.Equal((0x1000UL, (ulong)table.Length), ReadMemoryRequest(transport.SentPackets[1]));
        Assert.Equal((0x2000UL, 6UL), ReadMemoryRequest(transport.SentPackets[2]));
        Assert.Contains(transport.ReadCalls, call => call.TimeoutMilliseconds == 30000);
        Assert.DoesNotContain(transport.ReadCalls, call => call.TimeoutMilliseconds == 10);
        Assert.Equal(6UL, progress[^1].BytesCompleted);
        Assert.Equal(6UL, progress[^1].TotalBytes);
        Assert.Equal(1000, transport.TimeoutMilliseconds);
    }

    [Fact]
    public void CollectRamDumpAcceptsMemoryDebug64WithoutHello()
    {
        using var transport = new RamDumpRecordingTransport(
            TransportBackend.LibUsb,
            BuildMemoryDebug64(0x1000, 0),
            BuildSimplePacket(QualcommSaharaCommand.ResetResponse));
        using var output = new TemporaryDirectory();

        var regions = new QualcommSahara(transport).CollectRamDump(output.DirectoryPath);

        Assert.Empty(regions);
        Assert.Equal(
            [QualcommSaharaCommand.MemoryRead64Bit, QualcommSaharaCommand.Reset],
            transport.SentCommands);
    }

    [Fact]
    public void CollectRamDumpRecoversWhenWindowsQudDiscardsHello()
    {
        using var transport = new RamDumpRecordingTransport(
            TransportBackend.WindowsQud,
            new TimeoutException("discarded hello"),
            BuildMemoryDebug64(0x1000, 0),
            BuildSimplePacket(QualcommSaharaCommand.ResetResponse));
        using var output = new TemporaryDirectory();

        _ = new QualcommSahara(transport).CollectRamDump(output.DirectoryPath);

        Assert.Equal(QualcommSaharaCommand.HelloResponse, transport.SentCommands[0]);
        Assert.Equal(QualcommSaharaMode.MemoryDebug,
            (QualcommSaharaMode)BinaryPrimitives.ReadUInt32LittleEndian(transport.SentPackets[0].AsSpan(0x14)));
    }

    [Fact]
    public void CollectRamDumpDoesNotSpeculateAfterLibUsbTimeout()
    {
        using var transport = new RamDumpRecordingTransport(
            TransportBackend.LibUsb,
            new TimeoutException("no hello"));
        using var output = new TemporaryDirectory();

        _ = Assert.Throws<TimeoutException>(() =>
            new QualcommSahara(transport).CollectRamDump(output.DirectoryPath));

        Assert.Empty(transport.SentPackets);
    }

    [Fact]
    public void CollectRamDumpRejects32BitMemoryDebug()
    {
        using var transport = new RamDumpRecordingTransport(
            TransportBackend.WindowsQud,
            BuildSimplePacket(QualcommSaharaCommand.MemoryDebug));
        using var output = new TemporaryDirectory();

        _ = Assert.Throws<NotSupportedException>(() =>
            new QualcommSahara(transport).CollectRamDump(output.DirectoryPath));

        Assert.Equal([QualcommSaharaCommand.Reset], transport.SentCommands);
    }

    [Fact]
    public void CollectRamDumpRejectsHelloInAnotherMode()
    {
        using var transport = new RamDumpRecordingTransport(
            TransportBackend.WindowsQud,
            BuildHello(QualcommSaharaMode.Command));
        using var output = new TemporaryDirectory();

        _ = Assert.Throws<BadMessageException>(() =>
            new QualcommSahara(transport).CollectRamDump(output.DirectoryPath));

        Assert.Equal([QualcommSaharaCommand.Reset], transport.SentCommands);
    }

    [Fact]
    public void CollectRamDumpRejectsUnexpectedInitialCommand()
    {
        using var transport = new RamDumpRecordingTransport(
            TransportBackend.WindowsQud,
            BuildSimplePacket(QualcommSaharaCommand.Done));
        using var output = new TemporaryDirectory();

        _ = Assert.Throws<BadMessageException>(() =>
            new QualcommSahara(transport).CollectRamDump(output.DirectoryPath));

        Assert.Equal([QualcommSaharaCommand.Reset], transport.SentCommands);
    }

    [Fact]
    public void CollectRamDumpRejectsIncorrectMemoryDebugPacketLength()
    {
        using var transport = new RamDumpRecordingTransport(
            TransportBackend.WindowsQud,
            BuildSimplePacket(QualcommSaharaCommand.MemoryDebug64Bit));
        using var output = new TemporaryDirectory();

        _ = Assert.Throws<BadMessageException>(() =>
            new QualcommSahara(transport).CollectRamDump(output.DirectoryPath));
    }

    [Theory]
    [InlineData(65537UL)]
    [InlineData(1UL)]
    public void CollectRamDumpRejectsInvalidTableLength(ulong tableLength)
    {
        using var transport = new RamDumpRecordingTransport(
            TransportBackend.WindowsQud,
            BuildMemoryDebug64(0x1000, tableLength));
        using var output = new TemporaryDirectory();

        _ = Assert.Throws<BadMessageException>(() =>
            new QualcommSahara(transport).CollectRamDump(output.DirectoryPath));

        Assert.Equal([QualcommSaharaCommand.Reset], transport.SentCommands);
    }

    [Fact]
    public void CollectRamDumpRejectsOverflowingTableRange()
    {
        using var transport = new RamDumpRecordingTransport(
            TransportBackend.WindowsQud,
            BuildMemoryDebug64(ulong.MaxValue, 64));
        using var output = new TemporaryDirectory();

        _ = Assert.Throws<BadMessageException>(() =>
            new QualcommSahara(transport).CollectRamDump(output.DirectoryPath));
    }

    [Fact]
    public void CollectRamDumpRejectsShortTable()
    {
        using var transport = new RamDumpRecordingTransport(
            TransportBackend.WindowsQud,
            BuildMemoryDebug64(0x1000, 64),
            new byte[10],
            Array.Empty<byte>());
        using var output = new TemporaryDirectory();

        _ = Assert.Throws<BadMessageException>(() =>
            new QualcommSahara(transport).CollectRamDump(output.DirectoryPath));

        Assert.Equal(
            [QualcommSaharaCommand.MemoryRead64Bit, QualcommSaharaCommand.Reset],
            transport.SentCommands);
    }

    [Fact]
    public void CollectRamDumpRejectsOverflowingRegionRange()
    {
        var table = BuildTable(
            new RamDumpRegionDefinition(1, ulong.MaxValue, 2, "BAD", "bad.bin"));
        using var transport = new RamDumpRecordingTransport(
            TransportBackend.WindowsQud,
            BuildMemoryDebug64(0x1000, (ulong)table.Length),
            table);
        using var output = new TemporaryDirectory();

        _ = Assert.Throws<BadMessageException>(() =>
            new QualcommSahara(transport).CollectRamDump(output.DirectoryPath));
    }

    [Fact]
    public void CollectRamDumpChunksLargeRegionsAndAccumulatesShortReads()
    {
        var regionLength = (ulong)DebugBlockSize + 3;
        var table = BuildTable(new RamDumpRegionDefinition(1, 0x8000, regionLength, "DDR", "DDR.BIN"));
        var firstBlockPrefix = Enumerable.Repeat((byte)0xA5, 127).ToArray();
        var firstBlockRemainder = Enumerable.Repeat((byte)0x5A, DebugBlockSize - 127).ToArray();
        var lastBlock = new byte[] { 9, 8, 7 };
        using var transport = new RamDumpRecordingTransport(
            TransportBackend.LibUsb,
            BuildMemoryDebug64(0x1000, (ulong)table.Length),
            table,
            firstBlockPrefix,
            firstBlockRemainder,
            lastBlock,
            BuildSimplePacket(QualcommSaharaCommand.ResetResponse));
        using var output = new TemporaryDirectory();

        var regions = new QualcommSahara(transport).CollectRamDump(output.DirectoryPath);

        _ = Assert.Single(regions);
        var memoryReads = transport.SentPackets
            .Where(packet => ReadCommand(packet) == QualcommSaharaCommand.MemoryRead64Bit)
            .ToArray();
        Assert.Equal(3, memoryReads.Length);
        Assert.Equal((0x1000UL, (ulong)table.Length), ReadMemoryRequest(memoryReads[0]));
        Assert.Equal((0x8000UL, (ulong)DebugBlockSize), ReadMemoryRequest(memoryReads[1]));
        Assert.Equal((0x8000UL + DebugBlockSize, 3UL), ReadMemoryRequest(memoryReads[2]));

        var dumped = File.ReadAllBytes(output.GetPath("DDR.BIN"));
        Assert.Equal(checked((int)regionLength), dumped.Length);
        Assert.All(dumped.AsSpan(0, 127).ToArray(), value => Assert.Equal(0xA5, value));
        Assert.All(dumped.AsSpan(127, DebugBlockSize - 127).ToArray(), value => Assert.Equal(0x5A, value));
        Assert.Equal(lastBlock, dumped[^3..]);
    }

    [Fact]
    public void CollectRamDumpFiltersByFileNameAndStem()
    {
        var table = BuildTable(
            new RamDumpRegionDefinition(1, 0x1000, 0, "OCIMEM", "OCIMEM.BIN"),
            new RamDumpRegionDefinition(2, 0x2000, 0, "CODERAM", "CODERAM.bin"),
            new RamDumpRegionDefinition(3, 0x3000, 0, "KELF", "md_KELF"));
        using var transport = new RamDumpRecordingTransport(
            TransportBackend.LibUsb,
            BuildMemoryDebug64(0x4000, (ulong)table.Length),
            table,
            BuildSimplePacket(QualcommSaharaCommand.ResetResponse));
        using var output = new TemporaryDirectory();

        var regions = new QualcommSahara(transport).CollectRamDump(
            output.DirectoryPath,
            "OC?MEM,CODERAM");

        Assert.Equal(["OCIMEM.BIN", "CODERAM.bin"], regions.Select(region => region.FileName));
        Assert.True(File.Exists(output.GetPath("OCIMEM.BIN")));
        Assert.True(File.Exists(output.GetPath("CODERAM.bin")));
        Assert.False(File.Exists(output.GetPath("md_KELF")));
    }

    [Fact]
    public void CollectRamDumpDownloadsKelfAsAnOrdinaryRegion()
    {
        var table = BuildTable(new RamDumpRegionDefinition(3, 0x3000, 0, "KELF", "md_KELF"));
        using var transport = new RamDumpRecordingTransport(
            TransportBackend.LibUsb,
            BuildMemoryDebug64(0x4000, (ulong)table.Length),
            table,
            BuildSimplePacket(QualcommSaharaCommand.ResetResponse));
        using var output = new TemporaryDirectory();

        var regions = new QualcommSahara(transport).CollectRamDump(output.DirectoryPath);

        Assert.Equal("md_KELF", Assert.Single(regions).FileName);
        Assert.True(File.Exists(output.GetPath("md_KELF")));
        Assert.False(File.Exists(output.GetPath("minidump.elf")));
    }

    [Fact]
    public void CollectRamDumpSucceedsWhenFilterMatchesNoRegions()
    {
        var table = BuildTable(new RamDumpRegionDefinition(1, 0x1000, 0, "OCIMEM", "OCIMEM.BIN"));
        using var transport = new RamDumpRecordingTransport(
            TransportBackend.LibUsb,
            BuildMemoryDebug64(0x2000, (ulong)table.Length),
            table,
            BuildSimplePacket(QualcommSaharaCommand.ResetResponse));
        using var output = new TemporaryDirectory();

        var regions = new QualcommSahara(transport).CollectRamDump(output.DirectoryPath, "ocimem");

        Assert.Empty(regions);
        Assert.Empty(Directory.EnumerateFiles(output.DirectoryPath));
        Assert.Equal(QualcommSaharaCommand.Reset, transport.SentCommands[^1]);
    }

    [Fact]
    public void CollectRamDumpRejectsUnsafeFileName()
    {
        var table = BuildTable(new RamDumpRegionDefinition(1, 0x1000, 0, "BAD", "../outside.bin"));
        using var transport = new RamDumpRecordingTransport(
            TransportBackend.WindowsQud,
            BuildMemoryDebug64(0x2000, (ulong)table.Length),
            table);
        using var output = new TemporaryDirectory();

        _ = Assert.Throws<IOException>(() =>
            new QualcommSahara(transport).CollectRamDump(output.DirectoryPath));

        Assert.Equal(QualcommSaharaCommand.Reset, transport.SentCommands[^1]);
    }

    [Theory]
    [InlineData("NUL")]
    [InlineData("COM1.bin")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    public void CollectRamDumpRejectsNonPortableFileName(string fileName)
    {
        var table = BuildTable(new RamDumpRegionDefinition(1, 0x1000, 0, "BAD", fileName));
        using var transport = new RamDumpRecordingTransport(
            TransportBackend.WindowsQud,
            BuildMemoryDebug64(0x2000, (ulong)table.Length),
            table);
        using var output = new TemporaryDirectory();

        _ = Assert.Throws<IOException>(() =>
            new QualcommSahara(transport).CollectRamDump(output.DirectoryPath));
    }

    [Fact]
    public void CollectRamDumpRejectsDuplicateOutputNames()
    {
        var table = BuildTable(
            new RamDumpRegionDefinition(1, 0x1000, 0, "ONE", "same.bin"),
            new RamDumpRegionDefinition(2, 0x2000, 0, "TWO", "same.bin"));
        using var transport = new RamDumpRecordingTransport(
            TransportBackend.WindowsQud,
            BuildMemoryDebug64(0x3000, (ulong)table.Length),
            table);
        using var output = new TemporaryDirectory();

        _ = Assert.Throws<IOException>(() =>
            new QualcommSahara(transport).CollectRamDump(output.DirectoryPath));
    }

    [Fact]
    public void CollectRamDumpReplacesExistingFileOnlyAfterSuccess()
    {
        var table = BuildTable(new RamDumpRegionDefinition(1, 0x1000, 3, "DATA", "data.bin"));
        using var transport = new RamDumpRecordingTransport(
            TransportBackend.LibUsb,
            BuildMemoryDebug64(0x2000, (ulong)table.Length),
            table,
            new byte[] { 1, 2, 3 },
            BuildSimplePacket(QualcommSaharaCommand.ResetResponse));
        using var output = new TemporaryDirectory();
        File.WriteAllBytes(output.GetPath("data.bin"), [9, 9]);

        _ = new QualcommSahara(transport).CollectRamDump(output.DirectoryPath);

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(output.GetPath("data.bin")));
        Assert.False(File.Exists(output.GetPath("data.bin.partial")));
    }

    [Fact]
    public void CollectRamDumpPreservesPartialAndExistingFileAfterFailure()
    {
        var table = BuildTable(new RamDumpRegionDefinition(1, 0x1000, 5, "DATA", "data.bin"));
        using var transport = new RamDumpRecordingTransport(
            TransportBackend.LibUsb,
            BuildMemoryDebug64(0x2000, (ulong)table.Length),
            table,
            new byte[] { 1, 2 },
            new TimeoutException("data timeout"));
        using var output = new TemporaryDirectory();
        File.WriteAllBytes(output.GetPath("data.bin"), [9, 9]);

        _ = Assert.Throws<TimeoutException>(() =>
            new QualcommSahara(transport).CollectRamDump(output.DirectoryPath));

        Assert.Equal<byte>([9, 9], File.ReadAllBytes(output.GetPath("data.bin")));
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(output.GetPath("data.bin.partial")));
        Assert.Equal(QualcommSaharaCommand.Reset, transport.SentCommands[^1]);
    }

    [Fact]
    public void CollectRamDumpDoesNotSendSecondResetAfterInvalidResetResponse()
    {
        using var transport = new RamDumpRecordingTransport(
            TransportBackend.LibUsb,
            BuildMemoryDebug64(0x1000, 0),
            BuildSimplePacket(QualcommSaharaCommand.Done));
        using var output = new TemporaryDirectory();

        _ = Assert.Throws<BadMessageException>(() =>
            new QualcommSahara(transport).CollectRamDump(output.DirectoryPath));

        Assert.Equal(1, transport.SentCommands.Count(command => command == QualcommSaharaCommand.Reset));
    }

    private static byte[] BuildHello(QualcommSaharaMode mode)
    {
        var packet = new byte[0x30];
        BinaryPrimitives.WriteUInt32LittleEndian(packet, (uint)QualcommSaharaCommand.Hello);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0x04), (uint)packet.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0x08), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0x0C), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0x10), 1024 * 1024);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0x14), (uint)mode);
        return packet;
    }

    private static byte[] BuildMemoryDebug64(ulong tableAddress, ulong tableLength)
    {
        var packet = new byte[0x18];
        BinaryPrimitives.WriteUInt32LittleEndian(packet, (uint)QualcommSaharaCommand.MemoryDebug64Bit);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0x04), (uint)packet.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(0x08), tableAddress);
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(0x10), tableLength);
        return packet;
    }

    private static byte[] BuildSimplePacket(QualcommSaharaCommand command)
    {
        var packet = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(packet, (uint)command);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0x04), (uint)packet.Length);
        return packet;
    }

    private static byte[] BuildTable(params RamDumpRegionDefinition[] regions)
    {
        var table = new byte[regions.Length * 64];
        for (var index = 0; index < regions.Length; index++)
        {
            var record = table.AsSpan(index * 64, 64);
            var region = regions[index];
            BinaryPrimitives.WriteUInt64LittleEndian(record, region.Type);
            BinaryPrimitives.WriteUInt64LittleEndian(record[0x08..], region.Address);
            BinaryPrimitives.WriteUInt64LittleEndian(record[0x10..], region.Length);
            WriteFixedAscii(record.Slice(0x18, 20), region.RegionName);
            WriteFixedAscii(record.Slice(0x2C, 20), region.FileName);
        }

        return table;
    }

    private static void WriteFixedAscii(Span<byte> destination, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        Assert.True(bytes.Length <= destination.Length);
        bytes.CopyTo(destination);
    }

    private static QualcommSaharaCommand ReadCommand(byte[] packet)
    {
        return (QualcommSaharaCommand)BinaryPrimitives.ReadUInt32LittleEndian(packet);
    }

    private static (ulong Address, ulong Length) ReadMemoryRequest(byte[] packet)
    {
        return (
            BinaryPrimitives.ReadUInt64LittleEndian(packet.AsSpan(0x08)),
            BinaryPrimitives.ReadUInt64LittleEndian(packet.AsSpan(0x10)));
    }

    private sealed record RamDumpRegionDefinition(
        ulong Type,
        ulong Address,
        ulong Length,
        string RegionName,
        string FileName);
}

internal sealed class RamDumpRecordingTransport : IQualcommTransport
{
    private readonly LinkedList<object> _readSteps;

    internal RamDumpRecordingTransport(TransportBackend backend, params object[] readSteps)
    {
        Backend = backend;
        _readSteps = new(readSteps);
    }

    internal List<byte[]> SentPackets { get; } = [];
    internal List<QualcommSaharaCommand> SentCommands { get; } = [];
    internal List<(int TimeoutMilliseconds, int Count)> ReadCalls { get; } = [];

    public TransportBackend Backend { get; }
    public int TimeoutMilliseconds { get; set; } = 1000;

    public int Read(byte[] buffer, int offset, int count)
    {
        ReadCalls.Add((TimeoutMilliseconds, count));
        var step = _readSteps.First ??
            throw new InvalidOperationException("The test transport has no queued read response.");

        if (step.Value is Exception exception)
        {
            _readSteps.RemoveFirst();
            throw exception;
        }

        var response = Assert.IsType<byte[]>(step.Value);
        var bytesRead = Math.Min(count, response.Length);
        Buffer.BlockCopy(response, 0, buffer, offset, bytesRead);
        if (bytesRead == response.Length)
        {
            _readSteps.RemoveFirst();
        }
        else
        {
            step.Value = response[bytesRead..];
        }

        return bytesRead;
    }

    public int Write(byte[] buffer, int offset, int count)
    {
        var packet = buffer.AsSpan(offset, count).ToArray();
        SentPackets.Add(packet);
        SentCommands.Add((QualcommSaharaCommand)BinaryPrimitives.ReadUInt32LittleEndian(packet));
        return count;
    }

    public void SendZeroLengthPacket()
    {
    }

    public void Dispose()
    {
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    internal TemporaryDirectory()
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), $"QCEDL.NET.Tests-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(DirectoryPath);
    }

    internal string DirectoryPath { get; }

    internal string GetPath(string fileName)
    {
        return Path.Combine(DirectoryPath, fileName);
    }

    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath))
        {
            Directory.Delete(DirectoryPath, true);
        }
    }
}