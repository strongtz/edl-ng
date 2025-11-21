using System.Runtime.InteropServices;
using QCEDL.CLI.Helpers;
namespace QCEDL.CLI.Core;

/// <summary>
/// Manages direct access to host MTD devices for operations bypassing USB Firehose.
/// Currently supports SPI NOR flash devices only.
/// </summary>
internal sealed class HostDeviceManager : BlockDeviceManagerBase
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

        _isImageFile = !devicePath.StartsWith("/dev/", StringComparison.Ordinal);
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

        InitializeGeometry(_mtdInfo.Erasesize, imageSize);
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

        InitializeGeometry(_mtdInfo.Erasesize, _mtdInfo.Size);
    }

    protected override byte[] ReadFromStorage(ulong startOffset, uint readLength)
    {
        return _isImageFile
            ? ReadFromImageFile(startOffset, readLength)
            : ReadFromDevice(startOffset, readLength);
    }

    protected override void WriteAlignedRangeCore(ulong startOffset, byte[] alignedData, Action<long, long>? progressCallback)
    {
        if (_isImageFile)
        {
            WriteToImageFile(startOffset, alignedData, progressCallback);
            return;
        }

        var offset32 = checked((uint)startOffset);
        var length32 = checked((uint)alignedData.Length);

        EraseBlocks(offset32, length32);
        WriteData(offset32, alignedData, progressCallback);
    }

    protected override void ZeroRangeInternal(ulong startOffset, ulong length, Action<long, long>? progressCallback)
    {
        if (_isImageFile)
        {
            ZeroImageRange(startOffset, length, progressCallback);
            return;
        }

        var remaining = length;
        var currentOffset = startOffset;
        long processed = 0;
        var totalBytes = ClampToInt64(length);

        while (remaining > 0)
        {
            var chunkLength = (uint)Math.Min(uint.MaxValue, remaining);
            EraseBlocks(checked((uint)currentOffset), chunkLength);
            currentOffset += chunkLength;
            remaining -= chunkLength;
            processed = SafeAdvanceLong(processed, chunkLength);
            progressCallback?.Invoke(Math.Min(processed, totalBytes), totalBytes);
        }
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

    private void WriteToImageFile(ulong startOffset, byte[] data, Action<long, long>? progressCallback)
    {
        Logging.Log($"Writing {data.Length} bytes to image file at offset 0x{startOffset:X8}", LogLevel.Debug);

        _ = _imageStream!.Seek((long)startOffset, SeekOrigin.Begin);

        long totalWritten = 0;
        var writeSize = Math.Min(data.Length, (int)SectorSize);

        while (totalWritten < data.Length)
        {
            var remaining = data.Length - totalWritten;
            var currentWriteSize = Math.Min(writeSize, remaining);

            _imageStream.Write(data, (int)totalWritten, (int)currentWriteSize);
            _imageStream.Flush(); // Ensure data is written to disk

            totalWritten += currentWriteSize;
            progressCallback?.Invoke(totalWritten, data.Length);
        }

        Logging.Log($"Successfully wrote {data.Length} bytes to image file", LogLevel.Debug);
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

    private void ZeroImageRange(ulong startOffset, ulong length, Action<long, long>? progressCallback)
    {
        Logging.Log($"Zeroing {length} bytes in image file at offset 0x{startOffset:X8}", LogLevel.Debug);
        if (_imageStream == null)
        {
            throw new InvalidOperationException("Image stream is not initialized.");
        }

        _ = _imageStream.Seek((long)startOffset, SeekOrigin.Begin);
        var bufferSize = (int)Math.Min(DEFAULT_IMAGE_BLOCK_SIZE, length);
        bufferSize = bufferSize <= 0 ? (int)Math.Min(length, 4096UL) : bufferSize;

        var zeroBuffer = new byte[bufferSize];
        long processed = 0;
        var remaining = length;
        var totalBytes = ClampToInt64(length);

        while (remaining > 0)
        {
            var chunk = (int)Math.Min((ulong)zeroBuffer.Length, remaining);
            _imageStream.Write(zeroBuffer, 0, chunk);
            _imageStream.Flush();
            remaining -= (ulong)chunk;
            processed = SafeAdvance(processed, chunk);
            progressCallback?.Invoke(Math.Min(processed, totalBytes), totalBytes);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_deviceFd >= 0)
            {
                _ = close(_deviceFd);
                _deviceFd = -1;
            }

            _imageStream?.Dispose();
            _imageStream = null;
        }

        base.Dispose(disposing);
    }
}