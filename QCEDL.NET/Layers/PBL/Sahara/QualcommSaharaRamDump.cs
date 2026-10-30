using System.Buffers.Binary;
using System.Text;
using QCEDL.NET.Logging;
using Qualcomm.EmergencyDownload.Transport;

namespace Qualcomm.EmergencyDownload.Layers.PBL.Sahara;

internal sealed class QualcommSaharaRamDump(IQualcommTransport transport)
{
    private const int ControlTimeoutMilliseconds = 1000;
    private const int DataTimeoutMilliseconds = 30000;
    private const int ControlBufferSize = 4096;
    private const int DebugBlockSize = 1024 * 1024;
    private const int FileBufferSize = 1024 * 1024;
    private const int MaximumDebugTableLength = 64 * 1024;
    private const int DebugRegionRecordLength = 64;
    private const int FixedStringLength = 20;
    private const int HelloPacketLength = 0x30;
    private const int MemoryDebug64PacketLength = 0x18;
    private const int MemoryRead64PacketLength = 0x18;
    private const int ResetPacketLength = 0x08;

    private bool _protocolStarted;
    private bool _resetSent;

    public IReadOnlyList<QualcommSaharaRamDumpRegion> Collect(
        string outputDirectory,
        string? segmentFilter,
        Action<QualcommSaharaRamDumpProgress>? progress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var outputRoot = Path.GetFullPath(outputDirectory);
        _ = Directory.CreateDirectory(outputRoot);

        var originalTimeout = transport.TimeoutMilliseconds;
        try
        {
            transport.TimeoutMilliseconds = ControlTimeoutMilliseconds;
            var packet = ReadInitialPacket();
            var command = GetCommand(packet);

            if (command == QualcommSaharaCommand.Hello)
            {
                HandleHello(packet);
                packet = ReadControlPacket();
                command = GetCommand(packet);
            }

            if (command == QualcommSaharaCommand.MemoryDebug)
            {
                throw new NotSupportedException(
                    "32-bit Sahara memory debug packets are not supported. A 64-bit MEM_DEBUG64 packet is required.");
            }

            if (command != QualcommSaharaCommand.MemoryDebug64Bit)
            {
                throw new BadMessageException(
                    $"Expected Sahara MEM_DEBUG64, received command 0x{(uint)command:X8}.");
            }

            var regions = ReadRegionTable(packet);
            var selectedRegions = regions.Where(region => MatchesFilter(region.FileName, segmentFilter)).ToList();
            foreach (var skippedRegion in regions.Except(selectedRegions))
            {
                LibraryLogger.Debug($"Skipping ramdump region '{skippedRegion.FileName}' per segment filter.");
            }

            var outputPaths = ValidateOutputPaths(outputRoot, selectedRegions);
            var downloadedRegions = new List<QualcommSaharaRamDumpRegion>(selectedRegions.Count);

            foreach (var region in selectedRegions)
            {
                var (targetPath, partialPath) = outputPaths[region];
                DownloadRegion(region, targetPath, partialPath, progress);
                downloadedRegions.Add(region);
            }

            SendResetAndWait();
            return downloadedRegions;
        }
        catch
        {
            TryResetAfterFailure();
            throw;
        }
        finally
        {
            transport.TimeoutMilliseconds = originalTimeout;
        }
    }

    private byte[] ReadInitialPacket()
    {
        try
        {
            return ReadControlPacket();
        }
        catch (TimeoutException) when (transport.Backend == TransportBackend.WindowsQud)
        {
            LibraryLogger.Warning(
                "Initial Sahara ramdump read timed out on Windows QUD; sending a speculative MemoryDebug HELLO response.");
            _protocolStarted = true;
            transport.SendData(BuildHelloResponsePacket());
            return ReadControlPacket();
        }
    }

    private byte[] ReadControlPacket()
    {
        var packet = transport.GetResponse(null, ControlBufferSize);
        if (packet.AsSpan().StartsWith("<?xml"u8))
        {
            throw new BadMessageException("Received a Firehose XML response while collecting a Sahara ramdump.");
        }

        if (packet.Length < sizeof(uint) * 2)
        {
            throw new BadMessageException("Sahara response is shorter than the command header.");
        }

        var reportedLength = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(sizeof(uint), sizeof(uint)));
        if (reportedLength != packet.Length)
        {
            throw new BadMessageException(
                $"Sahara response length mismatch: header reports {reportedLength} bytes, received {packet.Length} bytes.");
        }

        _protocolStarted = true;
        return packet;
    }

    private void HandleHello(byte[] packet)
    {
        RequirePacketLength(packet, HelloPacketLength, "HELLO");
        var mode = (QualcommSaharaMode)BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(0x14, sizeof(uint)));
        if (mode != QualcommSaharaMode.MemoryDebug)
        {
            throw new BadMessageException(
                $"Expected a Sahara MemoryDebug HELLO, but the device reported {mode} mode.");
        }

        transport.SendData(BuildHelloResponsePacket());
    }

    private List<QualcommSaharaRamDumpRegion> ReadRegionTable(byte[] packet)
    {
        RequirePacketLength(packet, MemoryDebug64PacketLength, "MEM_DEBUG64");

        var tableAddress = BinaryPrimitives.ReadUInt64LittleEndian(packet.AsSpan(0x08, sizeof(ulong)));
        var tableLength = BinaryPrimitives.ReadUInt64LittleEndian(packet.AsSpan(0x10, sizeof(ulong)));
        if (tableLength > MaximumDebugTableLength)
        {
            throw new BadMessageException(
                $"Sahara debug table length 0x{tableLength:X} exceeds the 64 KiB limit.");
        }

        if (tableLength % DebugRegionRecordLength != 0)
        {
            throw new BadMessageException(
                $"Sahara debug table length {tableLength} is not aligned to the {DebugRegionRecordLength}-byte record size.");
        }

        ValidateAddressRange(tableAddress, tableLength, "Sahara debug table");
        var tableBuffer = new byte[checked((int)tableLength)];
        transport.SendData(BuildMemoryReadPacket(tableAddress, tableLength));
        if (tableBuffer.Length > 0)
        {
            ReadExactly(tableBuffer, "Sahara debug table");
        }

        var regions = new List<QualcommSaharaRamDumpRegion>(tableBuffer.Length / DebugRegionRecordLength);
        for (var offset = 0; offset < tableBuffer.Length; offset += DebugRegionRecordLength)
        {
            var record = tableBuffer.AsSpan(offset, DebugRegionRecordLength);
            var type = BinaryPrimitives.ReadUInt64LittleEndian(record);
            var address = BinaryPrimitives.ReadUInt64LittleEndian(record[0x08..]);
            var length = BinaryPrimitives.ReadUInt64LittleEndian(record[0x10..]);
            ValidateAddressRange(address, length, $"ramdump region at table offset 0x{offset:X}");

            regions.Add(new(
                type,
                address,
                length,
                ReadFixedAscii(record.Slice(0x18, FixedStringLength)),
                ReadFixedAscii(record.Slice(0x2C, FixedStringLength))));
        }

        return regions;
    }

    private static Dictionary<QualcommSaharaRamDumpRegion, (string TargetPath, string PartialPath)>
        ValidateOutputPaths(string outputRoot, IReadOnlyCollection<QualcommSaharaRamDumpRegion> regions)
    {
        var paths = new Dictionary<QualcommSaharaRamDumpRegion, (string, string)>();
        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var region in regions)
        {
            ValidateFileName(region.FileName);
            var targetPath = Path.GetFullPath(Path.Combine(outputRoot, region.FileName));
            var partialPath = targetPath + ".partial";
            EnsurePathIsInsideDirectory(outputRoot, targetPath);
            EnsurePathIsInsideDirectory(outputRoot, partialPath);

            if (!usedPaths.Add(targetPath) || !usedPaths.Add(partialPath))
            {
                throw new IOException(
                    $"Ramdump region '{region.FileName}' conflicts with another output or partial file name.");
            }

            if (Directory.Exists(targetPath) || Directory.Exists(partialPath))
            {
                throw new IOException($"Ramdump output path for '{region.FileName}' refers to a directory.");
            }

            paths.Add(region, (targetPath, partialPath));
        }

        return paths;
    }

    private void DownloadRegion(
        QualcommSaharaRamDumpRegion region,
        string targetPath,
        string partialPath,
        Action<QualcommSaharaRamDumpProgress>? progress)
    {
        LibraryLogger.Debug(
            $"Downloading ramdump region '{region.FileName}' at 0x{region.Address:X}, length 0x{region.Length:X}.");

        var buffer = new byte[DebugBlockSize];
        ulong completed = 0;
        using (var output = new FileStream(
                   partialPath,
                   FileMode.Create,
                   FileAccess.Write,
                   FileShare.Read,
                   FileBufferSize,
                   FileOptions.SequentialScan))
        {
            if (region.Length == 0)
            {
                progress?.Invoke(new(region, 0, 0));
            }

            while (completed < region.Length)
            {
                var requestLength = Math.Min(DebugBlockSize, region.Length - completed);
                var requestAddress = checked(region.Address + completed);

                transport.TimeoutMilliseconds = ControlTimeoutMilliseconds;
                transport.SendData(BuildMemoryReadPacket(requestAddress, requestLength));

                transport.TimeoutMilliseconds = DataTimeoutMilliseconds;
                var requestCompleted = 0;
                while ((ulong)requestCompleted < requestLength)
                {
                    var readLength = checked((int)(requestLength - (ulong)requestCompleted));
                    var bytesRead = transport.Read(buffer, 0, readLength);
                    if (bytesRead <= 0)
                    {
                        throw new IOException(
                            $"The device returned no data for ramdump region '{region.FileName}'.");
                    }

                    output.Write(buffer, 0, bytesRead);
                    requestCompleted += bytesRead;
                    var totalCompleted = completed + (ulong)requestCompleted;
                    progress?.Invoke(new(region, totalCompleted, region.Length));
                }

                completed += (ulong)requestCompleted;
            }
        }

        File.Move(partialPath, targetPath, true);
        LibraryLogger.Debug($"Ramdump region '{region.FileName}' downloaded successfully.");
    }

    private void ReadExactly(byte[] destination, string description)
    {
        var offset = 0;
        while (offset < destination.Length)
        {
            var bytesRead = transport.Read(destination, offset, destination.Length - offset);
            if (bytesRead <= 0)
            {
                throw new BadMessageException(
                    $"The device returned an incomplete {description}: received {offset} of {destination.Length} bytes.");
            }

            offset += bytesRead;
        }
    }

    private void SendResetAndWait()
    {
        transport.TimeoutMilliseconds = ControlTimeoutMilliseconds;
        _resetSent = true;
        transport.SendData(BuildSimpleCommandPacket(QualcommSaharaCommand.Reset));
        var response = ReadControlPacket();
        RequirePacketLength(response, ResetPacketLength, "RESET_RESP");
        var command = GetCommand(response);
        if (command != QualcommSaharaCommand.ResetResponse)
        {
            throw new BadMessageException(
                $"Expected Sahara RESET_RESP, received command 0x{(uint)command:X8}.");
        }
    }

    private void TryResetAfterFailure()
    {
        if (!_protocolStarted || _resetSent)
        {
            return;
        }

        try
        {
            transport.TimeoutMilliseconds = ControlTimeoutMilliseconds;
            _resetSent = true;
            transport.SendData(BuildSimpleCommandPacket(QualcommSaharaCommand.Reset));
        }
        catch (Exception ex)
        {
            LibraryLogger.Warning($"Best-effort Sahara reset after ramdump failure failed: {ex.Message}");
        }
    }

    private static byte[] BuildHelloResponsePacket()
    {
        var packet = new byte[HelloPacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet, (uint)QualcommSaharaCommand.HelloResponse);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0x04), HelloPacketLength);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0x08), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0x0C), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0x10), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0x14), (uint)QualcommSaharaMode.MemoryDebug);
        return packet;
    }

    private static byte[] BuildMemoryReadPacket(ulong address, ulong length)
    {
        var packet = new byte[MemoryRead64PacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet, (uint)QualcommSaharaCommand.MemoryRead64Bit);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0x04), MemoryRead64PacketLength);
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(0x08), address);
        BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(0x10), length);
        return packet;
    }

    private static byte[] BuildSimpleCommandPacket(QualcommSaharaCommand command)
    {
        var packet = new byte[ResetPacketLength];
        BinaryPrimitives.WriteUInt32LittleEndian(packet, (uint)command);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(0x04), ResetPacketLength);
        return packet;
    }

    private static QualcommSaharaCommand GetCommand(byte[] packet)
    {
        return (QualcommSaharaCommand)BinaryPrimitives.ReadUInt32LittleEndian(packet);
    }

    private static void RequirePacketLength(byte[] packet, int expectedLength, string packetName)
    {
        if (packet.Length != expectedLength)
        {
            throw new BadMessageException(
                $"Unexpected {packetName} packet length {packet.Length}; expected {expectedLength} bytes.");
        }
    }

    private static void ValidateAddressRange(ulong address, ulong length, string description)
    {
        if (length > ulong.MaxValue - address)
        {
            throw new BadMessageException(
                $"The {description} address range overflows 64-bit address space.");
        }
    }

    private static string ReadFixedAscii(ReadOnlySpan<byte> value)
    {
        var terminator = value.IndexOf((byte)0);
        return Encoding.ASCII.GetString(terminator >= 0 ? value[..terminator] : value);
    }

    private static bool MatchesFilter(string fileName, string? filter)
    {
        if (filter is null)
        {
            return true;
        }

        var dot = fileName.LastIndexOf('.');
        var stem = dot > 0 ? fileName[..dot] : null;
        foreach (var pattern in filter.Split(','))
        {
            if (WildcardMatches(pattern, fileName) ||
                (stem is not null && WildcardMatches(pattern, stem)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool WildcardMatches(string pattern, string value)
    {
        var patternIndex = 0;
        var valueIndex = 0;
        var starIndex = -1;
        var retryValueIndex = -1;

        while (valueIndex < value.Length)
        {
            if (patternIndex < pattern.Length &&
                (pattern[patternIndex] == '?' || pattern[patternIndex] == value[valueIndex]))
            {
                patternIndex++;
                valueIndex++;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex++;
                retryValueIndex = valueIndex;
            }
            else if (starIndex >= 0)
            {
                patternIndex = starIndex + 1;
                valueIndex = ++retryValueIndex;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }

    private static void ValidateFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName) || fileName is "." or ".." || Path.IsPathRooted(fileName) ||
            fileName.EndsWith(' ') || fileName.EndsWith('.') ||
            fileName.Contains('/') || fileName.Contains('\\') ||
            fileName.Any(character => character is < ' ' or > '~') ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
            IsWindowsReservedFileName(fileName))
        {
            throw new IOException($"Device supplied an unsafe ramdump file name: '{fileName}'.");
        }
    }

    private static bool IsWindowsReservedFileName(string fileName)
    {
        var dot = fileName.IndexOf('.');
        var stem = dot >= 0 ? fileName[..dot] : fileName;
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CLOCK$", StringComparison.OrdinalIgnoreCase) ||
            (stem.Length == 4 && stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
             stem[3] is >= '1' and <= '9') ||
            (stem.Length == 4 && stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase) &&
             stem[3] is >= '1' and <= '9');
    }

    private static void EnsurePathIsInsideDirectory(string outputRoot, string path)
    {
        var relativePath = Path.GetRelativePath(outputRoot, path);
        if (Path.IsPathRooted(relativePath) || relativePath == ".." ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new IOException($"Ramdump output path escapes the output directory: '{path}'.");
        }
    }
}
