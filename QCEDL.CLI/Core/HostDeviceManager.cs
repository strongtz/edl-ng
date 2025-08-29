using System.Globalization;
using System.Runtime.InteropServices;
using QCEDL.CLI.Helpers;
using QCEDL.NET.PartitionTable;

namespace QCEDL.CLI.Core;

/// <summary>
/// Manages direct access to host MTD devices for operations bypassing USB Firehose.
/// Currently supports SPI NOR flash devices only.
/// </summary>
internal sealed class HostDeviceManager : IDisposable
{
    private const uint DEFAULT_IMAGE_BLOCK_SIZE = 4096; // 4K blocks for image files
    private const ulong DEFAULT_IMAGE_SIZE = 32 * 1024 * 1024; // 32MB default
    private const uint MEMGETINFO = 0x80204D01;
    private const uint MEMERASE = 0x40084D02;
    private const int O_RDWR = 2;
    private const int O_SYNC = 0x1000;
    private const int SEEK_SET = 0;
    private const uint EXPECTED_BLOCK_SIZE = 4096; // 4K blocks for SPI NOR

    [StructLayout(LayoutKind.Sequential)]
    private struct MtdInfoUser
    {
        public byte Type;
        public uint Flags;
        public uint Size;
        public uint Erasesize;
        public uint Writesize;
        public uint Oobsize;
        public ulong Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EraseInfoUser
    {
        public uint Start;
        public uint Length;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int open(string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern nint read(int fd, IntPtr buf, nuint count);

    [DllImport("libc", SetLastError = true)]
    private static extern nint write(int fd, IntPtr buf, nuint count);

    [DllImport("libc", SetLastError = true)]
    private static extern long lseek(int fd, long offset, int whence);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, uint request, IntPtr argp);

    private readonly string _devicePath;
    private int _deviceFd = -1;
    private MtdInfoUser _mtdInfo;
    private bool _disposed;

    public uint SectorSize => _mtdInfo.Erasesize;
    public uint DeviceSize => _mtdInfo.Size;

    private readonly bool _isImageFile;
    private readonly string _imagePath;
    private FileStream? _imageStream;

    public HostDeviceManager(string devicePath, string? imgSize = null)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            throw new PlatformNotSupportedException("Host device target mode is only supported on Linux.");
        }

        _devicePath = devicePath ?? throw new ArgumentNullException(nameof(devicePath));

        _isImageFile = !devicePath.StartsWith("/dev/");
        _imagePath = devicePath;

        if (_isImageFile)
        {
            InitializeImageFile(imgSize);
        }
        else
        {
            if (!File.Exists(_devicePath))
            {
                throw new FileNotFoundException($"Host device not found: {_devicePath}");
            }
            InitializeDevice();
        }
    }

    private void InitializeImageFile(string? imgSize)
    {
        Logging.Log($"Initializing image file: {_imagePath}", LogLevel.Debug);

        var imageSize = DEFAULT_IMAGE_SIZE;

        if (!string.IsNullOrEmpty(imgSize))
        {
            if (!ImageSizeParser.TryParseSize(imgSize, out imageSize))
            {
                throw new ArgumentException($"Invalid image size format: {imgSize}. Use formats like 32M, 1G, 512K");
            }
        }

        if (imageSize == 0)
        {
            throw new ArgumentException("Image size cannot be zero");
        }

        if (imageSize % DEFAULT_IMAGE_BLOCK_SIZE != 0)
        {
            // Round up to nearest block boundary
            imageSize = (imageSize + DEFAULT_IMAGE_BLOCK_SIZE - 1) / DEFAULT_IMAGE_BLOCK_SIZE * DEFAULT_IMAGE_BLOCK_SIZE;
            Logging.Log($"Image size rounded up to block boundary: {imageSize} bytes", LogLevel.Info);
        }

        // Create directory if it doesn't exist
        var directory = Path.GetDirectoryName(_imagePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        // Create or open the image file
        var fileExists = File.Exists(_imagePath);

        if (fileExists)
        {
            var existingSize = new FileInfo(_imagePath).Length;
            if ((ulong)existingSize != imageSize)
            {
                Logging.Log($"Warning: Existing image file size ({existingSize} bytes) differs from specified size ({imageSize} bytes). Using existing file size.", LogLevel.Warning);
                imageSize = (ulong)existingSize;
            }
        }

        _imageStream = new FileStream(_imagePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        if (!fileExists)
        {
            // Create the image file with the specified size
            _imageStream.SetLength((long)imageSize);
            Logging.Log($"Created new image file: {_imagePath} ({imageSize} bytes)", LogLevel.Info);
        }

        // Set up the MTD info structure for image file mode
        _mtdInfo = new MtdInfoUser
        {
            Type = 0,
            Flags = 0,
            Size = (uint)imageSize,
            Erasesize = DEFAULT_IMAGE_BLOCK_SIZE,
            Writesize = DEFAULT_IMAGE_BLOCK_SIZE,
            Oobsize = 0
        };

        Logging.Log($"Image file initialized: {_imagePath}", LogLevel.Info);
        Logging.Log($"  Image size: {imageSize / (1024.0 * 1024.0):F2} MiB ({imageSize} bytes)", LogLevel.Info);
        Logging.Log($"  Block size: {DEFAULT_IMAGE_BLOCK_SIZE} bytes", LogLevel.Info);
    }

    private void InitializeDevice()
    {
        Logging.Log($"Opening host device: {_devicePath}", LogLevel.Debug);

        _deviceFd = open(_devicePath, O_RDWR | O_SYNC);
        if (_deviceFd < 0)
        {
            throw new InvalidOperationException($"Failed to open {_devicePath}: {Marshal.GetLastPInvokeErrorMessage()}");
        }

        // Get MTD device info
        var mtdPtr = Marshal.AllocHGlobal(Marshal.SizeOf<MtdInfoUser>());
        try
        {
            if (ioctl(_deviceFd, MEMGETINFO, mtdPtr) < 0)
            {
                throw new InvalidOperationException($"{_devicePath} is not a valid MTD flash device: {Marshal.GetLastPInvokeErrorMessage()}");
            }

            _mtdInfo = Marshal.PtrToStructure<MtdInfoUser>(mtdPtr);
        }
        finally
        {
            Marshal.FreeHGlobal(mtdPtr);
        }

        // Validate device properties
        if (_mtdInfo.Erasesize != EXPECTED_BLOCK_SIZE)
        {
            throw new InvalidOperationException($"Unsupported block size: {_mtdInfo.Erasesize} bytes. Expected {EXPECTED_BLOCK_SIZE} bytes for SPI NOR flash.");
        }

        Logging.Log($"Host device initialized: {_devicePath}", LogLevel.Info);
        Logging.Log($"  Device size: {_mtdInfo.Size / (1024.0 * 1024.0):F2} MiB ({_mtdInfo.Size} bytes)", LogLevel.Info);
        Logging.Log($"  Erase block size: {_mtdInfo.Erasesize} bytes", LogLevel.Info);
        Logging.Log($"  Write page size: {_mtdInfo.Writesize} bytes", LogLevel.Debug);
    }

    public byte[] ReadSectors(ulong startSector, uint sectorCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (sectorCount == 0)
        {
            throw new ArgumentException("Sector count must be greater than zero", nameof(sectorCount));
        }

        var startOffset = startSector * SectorSize;
        var readLength = sectorCount * SectorSize;

        return startOffset + readLength > DeviceSize
            ? throw new ArgumentException($"Read operation would exceed device bounds. Start: {startOffset}, Length: {readLength}, Device size: {DeviceSize}")
            : _isImageFile ? ReadFromImageFile(startOffset, readLength) : ReadFromDevice(startOffset, readLength);
    }

    private byte[] ReadFromImageFile(ulong startOffset, uint readLength)
    {
        Logging.Log($"Reading {readLength} bytes from image file at offset 0x{startOffset:X8}", LogLevel.Debug);

        _ = _imageStream!.Seek((long)startOffset, SeekOrigin.Begin);
        var buffer = new byte[readLength];
        var totalRead = 0;

        while (totalRead < readLength)
        {
            var bytesRead = _imageStream.Read(buffer, totalRead, (int)(readLength - totalRead));
            if (bytesRead == 0)
            {
                // Fill remaining with zeros if we hit EOF
                Array.Fill<byte>(buffer, 0, totalRead, (int)(readLength - totalRead));
                break;
            }
            totalRead += bytesRead;
        }

        return buffer;
    }

    private byte[] ReadFromDevice(ulong startOffset, uint readLength)
    {
        Logging.Log($"Reading {readLength} bytes from device {_devicePath} at offset 0x{startOffset:X8}", LogLevel.Debug);

        // Seek to the start position
        if (lseek(_deviceFd, (long)startOffset, SEEK_SET) < 0)
        {
            throw new InvalidOperationException($"Failed to seek to offset 0x{startOffset:X8} on {_devicePath}: {Marshal.GetLastPInvokeErrorMessage()}");
        }

        var buffer = new byte[readLength];
        var bufferPtr = Marshal.AllocHGlobal((int)readLength);

        try
        {
            var totalRead = 0;
            while (totalRead < readLength)
            {
                var remaining = (int)readLength - totalRead;
                var bytesToRead = Math.Min(remaining, (int)SectorSize);

                var bytesRead = read(_deviceFd, IntPtr.Add(bufferPtr, totalRead), (nuint)bytesToRead);

                if (bytesRead < 0)
                {
                    throw new InvalidOperationException($"Read failed at offset 0x{startOffset + (ulong)totalRead:X8} on {_devicePath}: {Marshal.GetLastPInvokeErrorMessage()}");
                }

                if (bytesRead == 0)
                {
                    throw new InvalidOperationException($"Unexpected end of device at offset 0x{startOffset + (ulong)totalRead:X8} on {_devicePath}");
                }

                totalRead += (int)bytesRead;
            }

            Marshal.Copy(bufferPtr, buffer, 0, (int)readLength);
            return buffer;
        }
        finally
        {
            Marshal.FreeHGlobal(bufferPtr);
        }
    }

    public void WriteSectors(ulong startSector, byte[] data, Action<long, long>? progressCallback = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (data == null || data.Length == 0)
        {
            throw new ArgumentException("Data cannot be null or empty", nameof(data));
        }

        var startOffset = startSector * SectorSize;
        var alignedLength = ((uint)data.Length + SectorSize - 1) / SectorSize * SectorSize;

        if (startOffset + alignedLength > DeviceSize)
        {
            throw new ArgumentException($"Write operation would exceed device bounds. Start: {startOffset}, Length: {alignedLength}, Device size: {DeviceSize}");
        }

        if (_isImageFile)
        {
            WriteToImageFile(startOffset, data, alignedLength, progressCallback);
        }
        else
        {
            WriteToDevice(startOffset, data, alignedLength, progressCallback);
        }
    }

    private void WriteToImageFile(ulong startOffset, byte[] data, uint alignedLength, Action<long, long>? progressCallback)
    {
        Logging.Log($"Writing {data.Length} bytes to image file at offset 0x{startOffset:X8}", LogLevel.Debug);

        // Prepare data with padding if necessary
        var writeData = data;
        if (data.Length != alignedLength)
        {
            Logging.Log($"Padding data from {data.Length} to {alignedLength} bytes", LogLevel.Debug);
            writeData = new byte[alignedLength];
            Array.Copy(data, writeData, data.Length);
        }

        _ = _imageStream!.Seek((long)startOffset, SeekOrigin.Begin);

        long totalWritten = 0;
        var writeSize = Math.Min(writeData.Length, (int)SectorSize);

        while (totalWritten < writeData.Length)
        {
            var remaining = writeData.Length - totalWritten;
            var currentWriteSize = Math.Min(writeSize, remaining);

            _imageStream.Write(writeData, (int)totalWritten, (int)currentWriteSize);
            _imageStream.Flush(); // Ensure data is written to disk

            totalWritten += currentWriteSize;
            progressCallback?.Invoke(totalWritten, writeData.Length);
        }

        Logging.Log($"Successfully wrote {data.Length} bytes to image file", LogLevel.Debug);
    }

    private void WriteToDevice(ulong startOffset, byte[] data, uint alignedLength, Action<long, long>? progressCallback)
    {
        Logging.Log($"Writing {data.Length} bytes to device {_devicePath} at offset 0x{startOffset:X8}", LogLevel.Debug);

        // Prepare data with padding if necessary
        var writeData = data;
        if (data.Length != alignedLength)
        {
            Logging.Log($"Padding data from {data.Length} to {alignedLength} bytes", LogLevel.Debug);
            writeData = new byte[alignedLength];
            Array.Copy(data, writeData, data.Length);
            // Rest is zero-padded automatically
        }

        // Erase affected blocks
        EraseBlocks((uint)startOffset, alignedLength);

        // Write data
        WriteData((uint)startOffset, writeData, progressCallback);

        Logging.Log($"Successfully wrote {data.Length} bytes to {_devicePath}", LogLevel.Debug);
    }

    private void EraseBlocks(uint startOffset, uint length)
    {
        // Align to erase block boundaries
        var eraseStart = startOffset / SectorSize * SectorSize;
        var eraseLength = ((startOffset + length + SectorSize - 1) / SectorSize * SectorSize) - eraseStart;

        Logging.Log($"Erasing blocks: 0x{eraseStart:X8} - 0x{eraseStart + eraseLength:X8} ({eraseLength} bytes)", LogLevel.Debug);

        var erase = new EraseInfoUser
        {
            Start = eraseStart,
            Length = eraseLength
        };

        var erasePtr = Marshal.AllocHGlobal(Marshal.SizeOf<EraseInfoUser>());
        try
        {
            Marshal.StructureToPtr(erase, erasePtr, false);

            if (ioctl(_deviceFd, MEMERASE, erasePtr) < 0)
            {
                throw new InvalidOperationException($"Failed to erase blocks 0x{eraseStart:X8}-0x{eraseStart + eraseLength:X8} on {_devicePath}: {Marshal.GetLastPInvokeErrorMessage()}");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(erasePtr);
        }

        Logging.Log($"Erased {eraseLength} bytes at offset 0x{eraseStart:X8}", LogLevel.Debug);
    }

    private void WriteData(uint offset, byte[] data, Action<long, long>? progressCallback = null)
    {
        // Seek to the start position
        if (lseek(_deviceFd, offset, SEEK_SET) < 0)
        {
            throw new InvalidOperationException($"Failed to seek to offset 0x{offset:X8} on {_devicePath}: {Marshal.GetLastPInvokeErrorMessage()}");
        }

        var dataPtr = Marshal.AllocHGlobal(data.Length);
        try
        {
            Marshal.Copy(data, 0, dataPtr, data.Length);

            long totalWritten = 0;
            var writeSize = Math.Min(data.Length, (int)SectorSize);

            while (totalWritten < data.Length)
            {
                var remaining = data.Length - totalWritten;
                var currentWriteSize = Math.Min(writeSize, remaining);

                var bytesWritten = write(_deviceFd, IntPtr.Add(dataPtr, (int)totalWritten), (nuint)currentWriteSize);

                if (bytesWritten != currentWriteSize)
                {
                    var errorMsg = bytesWritten < 0
                        ? Marshal.GetLastPInvokeErrorMessage()
                        : $"Short write: {bytesWritten}/{currentWriteSize} bytes";
                    throw new InvalidOperationException($"Write failed at offset 0x{offset + totalWritten:X8} on {_devicePath}: {errorMsg}");
                }

                totalWritten += bytesWritten;
                progressCallback?.Invoke(totalWritten, data.Length);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(dataPtr);
        }
    }

    public Gpt? ReadGpt()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Logging.Log($"Reading GPT from {_devicePath}", LogLevel.Debug);

        // Read enough sectors for GPT (64 sectors should be sufficient)
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
            Logging.Log($"Failed to parse GPT from {_devicePath}: {ex.Message}", LogLevel.Warning);
            return null;
        }
    }

    public GptPartition? FindPartition(string partitionName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(partitionName))
        {
            throw new ArgumentException("Partition name cannot be null or empty", nameof(partitionName));
        }

        var gpt = ReadGpt();
        if (gpt == null)
        {
            Logging.Log($"No GPT found on {_devicePath}, cannot search for partition '{partitionName}'", LogLevel.Warning);
            return null;
        }

        foreach (var partition in gpt.Partitions)
        {
            var currentPartitionName = partition.GetName().TrimEnd('\0');
            if (currentPartitionName.Equals(partitionName, StringComparison.OrdinalIgnoreCase))
            {
                Logging.Log($"Found partition '{partitionName}': LBA {partition.FirstLBA}-{partition.LastLBA}, Size: {(partition.LastLBA - partition.FirstLBA + 1) * SectorSize / 1024.0 / 1024.0:F2} MiB", LogLevel.Debug);
                return partition;
            }
        }

        Logging.Log($"Partition '{partitionName}' not found in GPT", LogLevel.Debug);
        return null;
    }

    public void WritePartition(string partitionName, byte[] data, Action<long, long>? progressCallback = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

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
            throw new ArgumentException($"Partition '{partitionName}' not found on device {_devicePath}");
        }

        var partitionSizeInBytes = (long)(partition.Value.LastLBA - partition.Value.FirstLBA + 1) * SectorSize;

        if (data.Length > partitionSizeInBytes)
        {
            throw new ArgumentException($"Data size ({data.Length} bytes) exceeds partition '{partitionName}' size ({partitionSizeInBytes} bytes)");
        }

        Logging.Log($"Writing {data.Length} bytes to partition '{partitionName}' on {_devicePath}", LogLevel.Info);
        Logging.Log($"Partition details: LBA {partition.Value.FirstLBA}-{partition.Value.LastLBA}, Size: {partitionSizeInBytes / 1024.0 / 1024.0:F2} MiB", LogLevel.Debug);

        // Write data starting at the partition's first LBA
        WriteSectors(partition.Value.FirstLBA, data, progressCallback);
    }

    private static readonly uint[] Crc32Table = GenerateCrc32Table();

    private const uint CRC32_SEED = 0;

    private static uint[] GenerateCrc32Table()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var crc = i;
            for (var j = 0; j < 8; j++)
            {
                if ((crc & 1) != 0)
                {
                    crc = (crc >> 1) ^ 0xEDB88320;
                }
                else
                {
                    crc >>= 1;
                }
            }
            table[i] = crc;
        }
        return table;
    }

    private static uint CalculateCrc32(ReadOnlySpan<byte> data, uint seed = CRC32_SEED)
    {
        var crc = 0xFFFFFFFF ^ seed;

        foreach (var b in data)
        {
            crc = (crc >> 8) ^ Crc32Table[(byte)(crc ^ b)];
        }

        return crc ^ 0xFFFFFFFF;
    }

    private uint CalculateCrc32OverSectors(ulong startSector, uint numBytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var crc = CRC32_SEED;
        var remainingBytes = numBytes;
        var currentSector = startSector;

        while (remainingBytes > 0)
        {
            var readSize = Math.Min(remainingBytes, SectorSize);
            var numSectors = ((readSize - 1) / SectorSize) + 1; // Round up to sector size

            var data = ReadSectors(currentSector, numSectors);

            // Only CRC over the actual requested bytes, not the full sectors
            var bytesToCrc = Math.Min((int)readSize, data.Length);
            crc = CalculateCrc32(data.AsSpan(0, bytesToCrc), crc);

            remainingBytes -= readSize;
            currentSector += numSectors;
        }

        return crc;
    }

    private bool TryParseValue(string valueStr, out ulong result)
    {
        result = 0;

        if (string.IsNullOrWhiteSpace(valueStr))
        {
            return false;
        }

        // Handle NUM_DISK_SECTORS expressions
        if (valueStr.StartsWith("NUM_DISK_SECTORS"))
        {
            var totalSectors = DeviceSize / SectorSize;

            if (valueStr.Length == "NUM_DISK_SECTORS".Length)
            {
                result = totalSectors;
                return true;
            }

            if (valueStr.Length > "NUM_DISK_SECTORS".Length + 1 && valueStr["NUM_DISK_SECTORS".Length] == '-')
            {
                var subtractStr = valueStr["NUM_DISK_SECTORS-".Length..];

                // Remove trailing dot if present
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

        // Handle regular numbers
        return TryParseNumber(valueStr, out result);
    }

    private static bool TryParseNumber(string str, out ulong result)
    {
        result = 0;

        if (string.IsNullOrWhiteSpace(str))
        {
            return false;
        }

        // Handle hex numbers
        if (str.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ulong.TryParse(str[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
        }

        // Handle decimal numbers
        return ulong.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    private bool TryParseCrc32Value(string valueStr, out uint result)
    {
        result = 0;

        if (!valueStr.StartsWith("CRC32(") || !valueStr.EndsWith(')'))
        {
            return false;
        }

        var innerContent = valueStr[6..^1]; // Remove "CRC32(" and ")"
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

    public void ApplyPatch(string startSectorStr, uint byteOffset, uint sizeInBytes, string valueStr)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

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

        // Parse start sector
        if (!TryParseValue(startSectorStr, out var startSector))
        {
            throw new ArgumentException($"Invalid start sector value: {startSectorStr}", nameof(startSectorStr));
        }

        // Parse patch value
        ulong patchValue;
        if (valueStr.StartsWith("CRC32("))
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

        // Read the sector
        var sectorData = ReadSectors(startSector, 1);

        // Apply the patch
        var patchBytes = new byte[8]; // Maximum size
        if (BitConverter.IsLittleEndian)
        {
            var valueBytes = BitConverter.GetBytes(patchValue);
            Array.Copy(valueBytes, patchBytes, Math.Min(valueBytes.Length, (int)sizeInBytes));
        }
        else
        {
            // Handle big-endian if needed (unlikely on most platforms)
            var valueBytes = BitConverter.GetBytes(patchValue);
            Array.Reverse(valueBytes);
            Array.Copy(valueBytes, patchBytes, Math.Min(valueBytes.Length, (int)sizeInBytes));
        }

        // Copy patch bytes to sector data
        Array.Copy(patchBytes, 0, sectorData, byteOffset, sizeInBytes);

        // Write the modified sector back
        WriteSectors(startSector, sectorData);

        Logging.Log($"Applied patch to sector {startSector} at offset {byteOffset}", LogLevel.Debug);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_deviceFd >= 0)
            {
                _ = close(_deviceFd);
                _deviceFd = -1;
            }

            _imageStream?.Dispose();
            _imageStream = null;

            _disposed = true;
        }
    }
}