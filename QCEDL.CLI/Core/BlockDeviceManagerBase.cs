using System.Buffers;
using System.Globalization;
using QCEDL.CLI.Helpers;
using QCEDL.NET.PartitionTable;

namespace QCEDL.CLI.Core;

internal abstract class BlockDeviceManagerBase : IDisposable
{
    private const uint STREAM_CHUNK_SECTORS = 1024;

    private static readonly uint[] Crc32Table = GenerateCrc32Table();

    private bool _disposed;

    public uint SectorSize { get; private set; }
    public ulong DeviceSize { get; private set; } = ulong.MaxValue;

    protected BlockDeviceManagerBase()
    {
    }

    protected void InitializeGeometry(uint sectorSize, ulong deviceSize)
    {
        ArgumentOutOfRangeException.ThrowIfZero(sectorSize, nameof(sectorSize));
        SectorSize = sectorSize;
        DeviceSize = deviceSize == 0 ? ulong.MaxValue : deviceSize;
    }

    public ulong TotalSectors => SectorSize == 0 || DeviceSize == ulong.MaxValue
        ? 0
        : DeviceSize / SectorSize;

    public virtual byte[] ReadSectors(ulong startSector, uint sectorCount)
    {
        ThrowIfDisposed();

        if (sectorCount == 0)
        {
            throw new ArgumentException("Sector count must be greater than zero.", nameof(sectorCount));
        }

        var startOffset = MultiplyOrThrow(startSector, SectorSize);
        var readLength = MultiplyOrThrow(sectorCount, SectorSize);

        EnsureWithinBounds(startOffset, readLength, "Read");

        ArgumentOutOfRangeException.ThrowIfGreaterThan(readLength, uint.MaxValue);
        return ReadFromStorage(startOffset, (uint)readLength);
    }

    public virtual void ReadSectorsToStream(
        ulong startSector,
        ulong sectorCount,
        Stream destination,
        Action<long, long>? progressCallback = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(destination);

        if (sectorCount == 0)
        {
            return;
        }

        var totalBytes = ClampToInt64(MultiplyOrMax(sectorCount, SectorSize));
        var remaining = sectorCount;
        var currentSector = startSector;
        var bytesWritten = 0L;

        while (remaining > 0)
        {
            var chunkSectors = (uint)Math.Min(STREAM_CHUNK_SECTORS, remaining);
            var data = ReadSectors(currentSector, chunkSectors);
            destination.Write(data, 0, data.Length);

            bytesWritten = SafeAdvance(bytesWritten, data.Length);
            progressCallback?.Invoke(bytesWritten, totalBytes);

            currentSector += chunkSectors;
            remaining -= chunkSectors;
        }
    }

    public void WriteStream(
        ulong startSector,
        Stream inputStream,
        long inputLength,
        bool padToSector,
        Action<long, long>? progressCallback = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(inputStream);

        if (!inputStream.CanRead)
        {
            throw new ArgumentException("Input stream must be readable.", nameof(inputStream));
        }

        if (inputLength < 0)
        {
            throw new ArgumentException("Input length cannot be negative.", nameof(inputLength));
        }

        var totalBytesToWrite = padToSector
            ? AlignmentHelper.AlignTo((ulong)inputLength, SectorSize)
            : (ulong)inputLength;

        if (totalBytesToWrite == 0)
        {
            Logging.Log("WriteStream: No data to write.", LogLevel.Debug);
            return;
        }

        var startOffset = MultiplyOrThrow(startSector, SectorSize);
        EnsureWithinBounds(startOffset, totalBytesToWrite, "Write");

        var totalBytesClamped = ClampToInt64(totalBytesToWrite);

        var bufferSize = (int)Math.Min((ulong)SectorSize * STREAM_CHUNK_SECTORS, totalBytesToWrite);
        bufferSize = Math.Max(bufferSize, (int)SectorSize);

        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            var remainingBytes = totalBytesToWrite;
            var remainingInputBytes = inputLength;
            var currentSector = startSector;
            long globalWritten = 0;

            while (remainingBytes > 0)
            {
                var chunkBytes = (int)Math.Min((ulong)bufferSize, remainingBytes);
                Array.Clear(buffer, 0, chunkBytes);

                var bytesFilled = 0;
                while (bytesFilled < chunkBytes && remainingInputBytes > 0)
                {
                    var read = inputStream.Read(buffer, bytesFilled,
                        (int)Math.Min(chunkBytes - bytesFilled, remainingInputBytes));
                    if (read == 0)
                    {
                        remainingInputBytes = 0;
                        break;
                    }

                    bytesFilled += read;
                    remainingInputBytes -= read;
                }

                var chunkData = new byte[chunkBytes];
                Buffer.BlockCopy(buffer, 0, chunkData, 0, Math.Min(bytesFilled, chunkBytes));

                var chunkStartOffset = MultiplyOrThrow(currentSector, SectorSize);

                void ChunkProgress(long chunkWritten, long chunkTotal)
                {
                    var absolute = Math.Min(globalWritten + chunkWritten, totalBytesClamped);
                    progressCallback?.Invoke(absolute, totalBytesClamped);
                }

                WriteAlignedRange(chunkStartOffset, chunkData, ChunkProgress);

                globalWritten = Math.Min(globalWritten + chunkData.Length, totalBytesClamped);
                progressCallback?.Invoke(globalWritten, totalBytesClamped);

                currentSector += (uint)(chunkData.Length / SectorSize);
                remainingBytes -= (ulong)chunkData.Length;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public void WriteSectors(ulong startSector, byte[] data, Action<long, long>? progressCallback = null)
    {
        ThrowIfDisposed();

        if (data == null || data.Length == 0)
        {
            throw new ArgumentException("Data cannot be null or empty.", nameof(data));
        }

        var startOffset = MultiplyOrThrow(startSector, SectorSize);
        var alignedLength = AlignmentHelper.AlignTo((ulong)data.Length, SectorSize);

        EnsureWithinBounds(startOffset, alignedLength, "Write");
        WriteAlignedRange(startOffset, data, progressCallback);
    }

    public void EraseSectors(ulong startSector, ulong sectorCount, Action<long, long>? progressCallback = null)
    {
        ThrowIfDisposed();

        if (sectorCount == 0)
        {
            throw new ArgumentException("Sector count must be greater than zero", nameof(sectorCount));
        }

        var startOffset = MultiplyOrThrow(startSector, SectorSize);
        var eraseLength = MultiplyOrThrow(sectorCount, SectorSize);

        EnsureWithinBounds(startOffset, eraseLength, "Erase");

        ZeroRangeInternal(startOffset, eraseLength, progressCallback);
    }

    public Gpt? ReadGpt()
    {
        ThrowIfDisposed();

        Logging.Log("Reading GPT from host target", LogLevel.Debug);

        const uint sectorsToRead = 64;
        var gptData = ReadSectors(0, sectorsToRead);

        if (gptData.Length < SectorSize * 2)
        {
            Logging.Log("Insufficient data read for GPT parsing", LogLevel.Warning);
            return null;
        }

        using var stream = new MemoryStream(gptData);
        try
        {
            var gpt = Gpt.ReadFromStream(stream, (int)SectorSize);
            if (gpt != null)
            {
                Logging.Log($"Successfully parsed GPT with {gpt.Partitions.Count} partitions", LogLevel.Debug);
            }
            return gpt;
        }
        catch (InvalidDataException ex)
        {
            Logging.Log($"Failed to parse GPT from host target: {ex.Message}", LogLevel.Warning);
            return null;
        }
    }

    public GptPartition? FindPartition(string partitionName)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(partitionName))
        {
            throw new ArgumentException("Partition name cannot be null or empty", nameof(partitionName));
        }

        var gpt = ReadGpt();
        if (gpt == null)
        {
            Logging.Log($"No GPT found on host target, cannot search for partition '{partitionName}'", LogLevel.Warning);
            return null;
        }

        foreach (var partition in gpt.Partitions)
        {
            var currentPartitionName = partition.GetName().TrimEnd('\0');
            if (currentPartitionName.Equals(partitionName, StringComparison.OrdinalIgnoreCase))
            {
                Logging.Log($"Found partition '{partitionName}': LBA {partition.FirstLBA}-{partition.LastLBA}", LogLevel.Debug);
                return partition;
            }
        }

        Logging.Log($"Partition '{partitionName}' not found in GPT", LogLevel.Debug);
        return null;
    }

    public void WritePartition(string partitionName, byte[] data, Action<long, long>? progressCallback = null)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(partitionName))
        {
            throw new ArgumentException("Partition name cannot be null or empty", nameof(partitionName));
        }

        if (data == null || data.Length == 0)
        {
            throw new ArgumentException("Data cannot be null or empty", nameof(data));
        }

        var partition = FindPartition(partitionName);
        if (!partition.HasValue)
        {
            throw new ArgumentException($"Partition '{partitionName}' not found on host target");
        }

        var partitionSizeInBytes = (long)(partition.Value.LastLBA - partition.Value.FirstLBA + 1) * SectorSize;

        if (data.Length > partitionSizeInBytes)
        {
            throw new ArgumentException($"Data size ({data.Length} bytes) exceeds partition '{partitionName}' size ({partitionSizeInBytes} bytes)");
        }

        Logging.Log($"Writing {data.Length} bytes to partition '{partitionName}'", LogLevel.Info);
        WriteSectors(partition.Value.FirstLBA, data, progressCallback);
    }

    public void ApplyPatch(string startSectorStr, uint byteOffset, uint sizeInBytes, string valueStr)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(startSectorStr))
        {
            throw new ArgumentException("Start sector cannot be null or empty", nameof(startSectorStr));
        }

        if (string.IsNullOrWhiteSpace(valueStr))
        {
            throw new ArgumentException("Value cannot be null or empty", nameof(valueStr));
        }

        if (sizeInBytes is 0 or > 8)
        {
            throw new ArgumentException("Size in bytes must be between 1 and 8", nameof(sizeInBytes));
        }

        if (byteOffset >= SectorSize)
        {
            throw new ArgumentException($"Byte offset ({byteOffset}) must be less than sector size ({SectorSize})", nameof(byteOffset));
        }

        if (!TryParseValue(startSectorStr, out var startSector))
        {
            throw new ArgumentException($"Invalid start sector value: {startSectorStr}", nameof(startSectorStr));
        }

        ulong patchValue;
        if (valueStr.StartsWith("CRC32(", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseCrc32Value(valueStr, out var crc32Value))
            {
                throw new ArgumentException($"Invalid CRC32 expression: {valueStr}", nameof(valueStr));
            }

            patchValue = crc32Value;
        }
        else
        {
            if (!TryParseValue(valueStr, out patchValue))
            {
                throw new ArgumentException($"Invalid patch value: {valueStr}", nameof(valueStr));
            }
        }

        Logging.Log($"Applying patch: sector {startSector}, offset {byteOffset}, size {sizeInBytes}, value 0x{patchValue:X}", LogLevel.Debug);

        var sectorData = ReadSectors(startSector, 1);

        var patchBytes = new byte[8];
        if (BitConverter.IsLittleEndian)
        {
            var valueBytes = BitConverter.GetBytes(patchValue);
            Array.Copy(valueBytes, patchBytes, Math.Min(valueBytes.Length, (int)sizeInBytes));
        }
        else
        {
            var valueBytes = BitConverter.GetBytes(patchValue);
            Array.Reverse(valueBytes);
            Array.Copy(valueBytes, patchBytes, Math.Min(valueBytes.Length, (int)sizeInBytes));
        }

        Array.Copy(patchBytes, 0, sectorData, byteOffset, sizeInBytes);

        WriteSectors(startSector, sectorData);

        Logging.Log($"Applied patch to sector {startSector} at offset {byteOffset}", LogLevel.Debug);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        _disposed = true;
    }

    protected abstract byte[] ReadFromStorage(ulong startOffset, uint readLength);

    protected abstract void WriteAlignedRangeCore(ulong startOffset, byte[] alignedData, Action<long, long>? progressCallback);

    protected abstract void ZeroRangeInternal(ulong startOffset, ulong length, Action<long, long>? progressCallback);

    protected static ulong MultiplyOrThrow(ulong value, uint multiplier)
    {
        try
        {
            return checked(value * multiplier);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, $"Operation would overflow 64-bit range: {value} * {multiplier}");
        }
    }

    protected static ulong MultiplyOrMax(ulong value, uint multiplier)
    {
        try
        {
            return checked(value * multiplier);
        }
        catch (OverflowException)
        {
            return ulong.MaxValue;
        }
    }

    protected static long SafeAdvance(long current, int delta)
    {
        return current > long.MaxValue - delta ? long.MaxValue : current + delta;
    }

    protected static long SafeAdvanceLong(long current, uint delta)
    {
        return current > long.MaxValue - delta ? long.MaxValue : current + delta;
    }

    protected static long ClampToInt64(ulong value)
    {
        return value > long.MaxValue ? long.MaxValue : (long)value;
    }

    private void WriteAlignedRange(ulong startOffset, byte[] data, Action<long, long>? progressCallback)
    {
        var alignedLength = AlignmentHelper.AlignTo((ulong)data.Length, SectorSize);
        byte[] dataToWrite;

        if (alignedLength == (ulong)data.Length)
        {
            dataToWrite = data;
        }
        else
        {
            dataToWrite = new byte[(int)alignedLength];
            Buffer.BlockCopy(data, 0, dataToWrite, 0, data.Length);
        }

        WriteAlignedRangeCore(startOffset, dataToWrite, progressCallback);
    }

    private void EnsureWithinBounds(ulong startOffset, ulong length, string operation)
    {
        if (DeviceSize == ulong.MaxValue)
        {
            return;
        }

        if (!TryAdd(startOffset, length, out var endOffset) || endOffset > DeviceSize)
        {
            throw new ArgumentException(
                $"{operation} operation would exceed device bounds. Start: {startOffset}, Length: {length}, Device size: {DeviceSize}");
        }
    }

    private static bool TryAdd(ulong a, ulong b, out ulong result)
    {
        result = a + b;
        return result >= a;
    }

    private uint CalculateCrc32OverSectors(ulong startSector, uint numBytes)
    {
        ThrowIfDisposed();

        var crc = 0u;
        var remainingBytes = numBytes;
        var currentSector = startSector;

        while (remainingBytes > 0)
        {
            var readSize = Math.Min(remainingBytes, SectorSize);
            var numSectors = ((readSize - 1) / SectorSize) + 1;

            var data = ReadSectors(currentSector, numSectors);

            var bytesToCrc = Math.Min((int)readSize, data.Length);
            crc = CalculateCrc32(data.AsSpan(0, bytesToCrc), crc);

            remainingBytes -= readSize;
            currentSector += numSectors;
        }

        return crc;
    }

    private static uint CalculateCrc32(ReadOnlySpan<byte> data, uint seed = 0)
    {
        var crc = 0xFFFFFFFF ^ seed;

        foreach (var b in data)
        {
            crc = (crc >> 8) ^ Crc32Table[(byte)(crc ^ b)];
        }

        return crc ^ 0xFFFFFFFF;
    }

    private static uint[] GenerateCrc32Table()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var crc = i;
            for (var j = 0; j < 8; j++)
            {
                crc = (crc & 1) != 0
                    ? (crc >> 1) ^ 0xEDB88320
                    : crc >> 1;
            }

            table[i] = crc;
        }

        return table;
    }

    private bool TryParseValue(string valueStr, out ulong result)
    {
        result = 0;

        if (string.IsNullOrWhiteSpace(valueStr))
        {
            return false;
        }

        if (valueStr.StartsWith("NUM_DISK_SECTORS", StringComparison.OrdinalIgnoreCase))
        {
            var totalSectors = TotalSectors;

            if (totalSectors == 0)
            {
                return false;
            }

            if (valueStr.Length == "NUM_DISK_SECTORS".Length)
            {
                result = totalSectors;
                return true;
            }

            if (valueStr.Length > "NUM_DISK_SECTORS".Length + 1 && valueStr["NUM_DISK_SECTORS".Length] == '-')
            {
                var subtractStr = valueStr["NUM_DISK_SECTORS-".Length..];

                if (subtractStr.EndsWith('.'))
                {
                    subtractStr = subtractStr[..^1];
                }

                if (TryParseNumber(subtractStr, out var subtractValue))
                {
                    if (totalSectors >= subtractValue)
                    {
                        result = totalSectors - subtractValue;
                        return true;
                    }
                }
            }

            return false;
        }

        return TryParseNumber(valueStr, out result);
    }

    private static bool TryParseNumber(string str, out ulong result)
    {
        result = 0;

        return !string.IsNullOrWhiteSpace(str) && (
            str.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? ulong.TryParse(str[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result)
                : ulong.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out result));
    }

    private bool TryParseCrc32Value(string valueStr, out uint result)
    {
        result = 0;

        if (!valueStr.StartsWith("CRC32(", StringComparison.OrdinalIgnoreCase) || !valueStr.EndsWith(')'))
        {
            return false;
        }

        var innerContent = valueStr[6..^1];
        var parts = innerContent.Split(',');

        if (parts.Length != 2)
        {
            return false;
        }

        var startSectorStr = parts[0].Trim();
        var numBytesStr = parts[1].Trim();

        if (!TryParseValue(startSectorStr, out var startSector))
        {
            return false;
        }

        if (!TryParseNumber(numBytesStr, out var numBytesUlong))
        {
            return false;
        }

        if (numBytesUlong > uint.MaxValue)
        {
            return false;
        }

        var numBytes = (uint)numBytesUlong;

        try
        {
            result = CalculateCrc32OverSectors(startSector, numBytes);
            return true;
        }
        catch (Exception ex)
        {
            Logging.Log($"Failed to calculate CRC32 for sectors {startSector}, {numBytes} bytes: {ex.Message}", LogLevel.Error);
            return false;
        }
    }

    protected void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}