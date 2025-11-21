using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using QCEDL.CLI.Helpers;
using Vanara.PInvoke;
using FileAccess = Vanara.PInvoke.Kernel32.FileAccess;

namespace QCEDL.CLI.Core;

internal sealed class RadxaWoSDeviceManager : BlockDeviceManagerBase
{
    private const string DEVICE_PATH = @"\\.\RadxaPlatform";

    private const uint RADXA_PLATFORM_TYPE = 40000;
    private const uint RADXA_PLATFORM_SPINOR_RW_MAX_BYTES = 1024 * 1024;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private const uint METHOD_BUFFERED = 0;
    private const uint FILE_ANY_ACCESS = 0;

    private const uint MAX_PARAMETERS = 10;
    private const uint MAX_RESULTS = 4;
    private const int TZ_SECURE_APP_NAME_SIZE = 256;
    private const int SECSVC_BUFFER_SIZE = 4096;

    private static readonly uint IOCTL_RADXA_PLATFORM_SEND_SECURE_CMD =
        CtLCode(RADXA_PLATFORM_TYPE, 0xA16, METHOD_BUFFERED, FILE_ANY_ACCESS);

    private static readonly uint IOCTL_RADXA_PLATFORM_SPINOR_READ_SECTORS =
        CtLCode(RADXA_PLATFORM_TYPE, 0xA30, METHOD_BUFFERED, FILE_ANY_ACCESS);

    private static readonly uint IOCTL_RADXA_PLATFORM_SPINOR_WRITE_SECTORS =
        CtLCode(RADXA_PLATFORM_TYPE, 0xA31, METHOD_BUFFERED, FILE_ANY_ACCESS);

    private readonly SafeFileHandle _deviceHandle;
    private readonly ulong _maxSectorsPerRequest;

    public RadxaWoSDeviceManager()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException("Radxa WoS backend is only available on Windows.");
        }

        _deviceHandle = CreateFileW(
            DEVICE_PATH,
            (uint)(FileAccess.GENERIC_READ | FileAccess.GENERIC_WRITE),
            (uint)(FileShare.Read | FileShare.Write),
            IntPtr.Zero,
            (uint)FileMode.Open,
            (uint)FileFlagsAndAttributes.FILE_ATTRIBUTE_NORMAL,
            IntPtr.Zero);

        if (_deviceHandle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to open {DEVICE_PATH}.");
        }

        var storageInfo = QueryStorageInfo();
        var sectorSize = storageInfo.BlockSize != 0 ? storageInfo.BlockSize : storageInfo.PageSize;
        if (sectorSize == 0)
        {
            throw new InvalidOperationException("Unable to determine sector size from Radxa platform.");
        }

        var totalBlocks = storageInfo.TotalBlocks;
        var deviceSize = totalBlocks > 0 ? totalBlocks * sectorSize : ulong.MaxValue;

        InitializeGeometry(sectorSize, deviceSize);

        _maxSectorsPerRequest = Math.Max(1u, RADXA_PLATFORM_SPINOR_RW_MAX_BYTES / sectorSize);

        Logging.Log(
            $"Radxa WoS SPI NOR initialized: sector={sectorSize} bytes, blocks={(totalBlocks == 0 ? "unknown" : totalBlocks)}",
            LogLevel.Info);
    }

    protected override byte[] ReadFromStorage(ulong startOffset, uint readLength)
    {
        ThrowIfDisposed();

        var result = new byte[readLength];
        var remainingBytes = readLength;
        var writeOffset = 0;
        var currentLba = startOffset / SectorSize;

        while (remainingBytes > 0)
        {
            var remainingSectors = ((ulong)remainingBytes + SectorSize - 1) / SectorSize;
            var chunkSectors = (uint)Math.Min(_maxSectorsPerRequest, remainingSectors);
            var expectedBytes = chunkSectors * SectorSize;

            var data = SendSpinorReadRequest(currentLba, chunkSectors, expectedBytes);
            var bytesToCopy = (int)Math.Min(expectedBytes, remainingBytes);

            Buffer.BlockCopy(data, 0, result, writeOffset, bytesToCopy);

            writeOffset += bytesToCopy;
            remainingBytes -= (uint)bytesToCopy;
            currentLba += chunkSectors;
        }

        return result;
    }

    protected override void WriteAlignedRangeCore(ulong startOffset, byte[] alignedData, Action<long, long>? progressCallback)
    {
        ThrowIfDisposed();

        var remainingBytes = alignedData.Length;
        var readOffset = 0;
        var currentLba = startOffset / SectorSize;
        long written = 0;

        var maxChunkBytes = (int)(_maxSectorsPerRequest * SectorSize);

        while (remainingBytes > 0)
        {
            var chunkBytes = Math.Min(remainingBytes, maxChunkBytes);
            var chunkSectors = (uint)(chunkBytes / SectorSize);

            SendSpinorWriteRequest(currentLba, chunkSectors, alignedData.AsSpan(readOffset, chunkBytes));
            Logging.Log($"Radxa WoS write chunk: LBA {currentLba}, sectors {chunkSectors}, bytes {chunkBytes}", LogLevel.Trace);

            readOffset += chunkBytes;
            remainingBytes -= chunkBytes;
            currentLba += chunkSectors;

            written += chunkBytes;
            progressCallback?.Invoke(written, alignedData.Length);
        }
    }

    protected override void ZeroRangeInternal(ulong startOffset, ulong length, Action<long, long>? progressCallback)
    {
        ThrowIfDisposed();

        var zeroChunkSize = (int)(_maxSectorsPerRequest * SectorSize);
        var zeroBuffer = new byte[zeroChunkSize];

        var remaining = length;
        var currentOffset = startOffset;
        long written = 0;
        var totalBytes = ClampToInt64(length);

        while (remaining > 0)
        {
            var chunkBytes = (int)Math.Min((ulong)zeroBuffer.Length, remaining);
            var chunkSectors = (uint)(chunkBytes / SectorSize);
            if (chunkBytes % SectorSize != 0)
            {
                chunkSectors++;
                chunkBytes = (int)(chunkSectors * SectorSize);
            }

            SendSpinorWriteRequest(currentOffset / SectorSize, chunkSectors, zeroBuffer.AsSpan(0, chunkBytes));
            Logging.Log($"Radxa WoS zero chunk: LBA {currentOffset / SectorSize}, sectors {chunkSectors}, bytes {chunkBytes}", LogLevel.Trace);

            remaining -= (ulong)chunkBytes;
            currentOffset += (ulong)chunkBytes;
            written = SafeAdvanceLong(written, (uint)chunkBytes);
            progressCallback?.Invoke(Math.Min(written, totalBytes), totalBytes);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _deviceHandle.Dispose();
        }

        base.Dispose(disposing);
    }

    private TzStorageInfo QueryStorageInfo()
    {
        var sendCmd = CreateSendCommand((uint)RadxaPlatformCommand.QueryStorageInfo);
        var sendBytes = StructureToBytes(sendCmd);

        var responseSize = Marshal.SizeOf<RadxaPlatformGetResponse>() + Marshal.SizeOf<TzStorageInfo>();
        var response = new byte[responseSize];

        ExecuteIoControl(IOCTL_RADXA_PLATFORM_SEND_SECURE_CMD, sendBytes, response);

        var header = BytesToStructure<RadxaPlatformGetResponse>(response);
        return header.ErrorVal == 0
            ? BytesToStructure<TzStorageInfo>(response, Marshal.SizeOf<RadxaPlatformGetResponse>())
            : throw new InvalidOperationException($"Radxa storage info query failed with status 0x{header.ErrorVal:X8}");
    }

    private byte[] SendSpinorReadRequest(ulong startLba, uint sectorCount, uint expectedBytes)
    {
        Logging.Log($"Radxa WoS read: LBA {startLba}, sectors {sectorCount}, bytes {expectedBytes}", LogLevel.Trace);

        var request = new QcSpinorRwRequest
        {
            StartLba = startLba,
            SectorCount = sectorCount
        };

        var requestBytes = StructureToBytes(request);
        var headerSize = Marshal.SizeOf<QcSpinorReadResponseHeader>();
        var responseSize = headerSize + (int)expectedBytes;

        while (true)
        {
            var output = new byte[responseSize];
            if (TryExecuteIoControl(IOCTL_RADXA_PLATFORM_SPINOR_READ_SECTORS, requestBytes, output, out var lastError))
            {
                var header = BytesToStructure<QcSpinorReadResponseHeader>(output);
                var transferBytes = Math.Min(header.TransferBytes, (uint)(output.Length - headerSize));

                if (transferBytes < expectedBytes)
                {
                    throw new InvalidOperationException($"Spinor read returned {transferBytes} bytes, expected {expectedBytes} (LBA {startLba}, sectors {sectorCount}).");
                }

                var data = new byte[expectedBytes];
                Buffer.BlockCopy(output, headerSize, data, 0, (int)expectedBytes);
                return data;
            }

            if (lastError == ERROR_INSUFFICIENT_BUFFER && responseSize < headerSize + ((int)RADXA_PLATFORM_SPINOR_RW_MAX_BYTES * 2))
            {
                responseSize += (int)SectorSize;
                Logging.Log($"Radxa WoS read buffer too small (LBA {startLba}, sectors {sectorCount}), expanding to {responseSize} bytes", LogLevel.Trace);
                continue;
            }

            throw new Win32Exception(lastError, $"DeviceIoControl 0x{IOCTL_RADXA_PLATFORM_SPINOR_READ_SECTORS:X8} failed for read LBA {startLba} sectors {sectorCount}.");
        }
    }

    private void SendSpinorWriteRequest(ulong startLba, uint sectorCount, ReadOnlySpan<byte> input)
    {
        var expectedLength = (int)(sectorCount * SectorSize);
        if (input.Length != expectedLength)
        {
            throw new ArgumentException($"Spinor write payload length ({input.Length}) does not match sector count ({sectorCount}).", nameof(input));
        }

        var request = new QcSpinorRwRequest
        {
            StartLba = startLba,
            SectorCount = sectorCount
        };

        var headerBytes = StructureToBytes(request);
        var inputBuffer = new byte[headerBytes.Length + expectedLength];
        Buffer.BlockCopy(headerBytes, 0, inputBuffer, 0, headerBytes.Length);
        input.CopyTo(inputBuffer.AsSpan(headerBytes.Length));

        ExecuteIoControl(IOCTL_RADXA_PLATFORM_SPINOR_WRITE_SECTORS, inputBuffer, null);
    }

    private void ExecuteIoControl(uint controlCode, byte[]? input, byte[]? output)
    {
        if (!TryExecuteIoControl(controlCode, input, output, out var lastError))
        {
            throw new Win32Exception(lastError, $"DeviceIoControl 0x{controlCode:X8} failed.");
        }
    }

    private bool TryExecuteIoControl(uint controlCode, byte[]? input, byte[]? output, out int lastError)
    {
        var success = DeviceIoControl(
            _deviceHandle,
            controlCode,
            input,
            input?.Length ?? 0,
            output,
            output?.Length ?? 0,
            out _,
            IntPtr.Zero);

        if (!success)
        {
            lastError = Marshal.GetLastWin32Error();
            return false;
        }

        lastError = 0;
        return true;
    }

    private static RadxaPlatformSendCommand CreateSendCommand(uint command)
    {
        return new RadxaPlatformSendCommand
        {
            Cmd = command,
            Parameters = new ulong[MAX_PARAMETERS],
            AppName = new byte[TZ_SECURE_APP_NAME_SIZE],
            InBuffer = new byte[SECSVC_BUFFER_SIZE]
        };
    }

    private static byte[] StructureToBytes<T>(T value)
        where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var buffer = new byte[size];
        var ptr = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(value, ptr, false);
            Marshal.Copy(ptr, buffer, 0, size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        return buffer;
    }

    private static T BytesToStructure<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>(byte[] buffer, int offset = 0)
        where T : struct
    {
        var size = Marshal.SizeOf<T>();
        if (buffer.Length - offset < size)
        {
            throw new ArgumentException("Buffer is smaller than required structure size.", nameof(buffer));
        }

        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(buffer, offset, ptr, size);
            return Marshal.PtrToStructure<T>(ptr)!;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static uint CtLCode(uint deviceType, uint function, uint method, uint access)
    {
        return (deviceType << 16) | (access << 14) | (function << 2) | method;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        byte[]? lpInBuffer,
        int nInBufferSize,
        byte[]? lpOutBuffer,
        int nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    private enum RadxaPlatformCommand : uint
    {
        QuerySpinorWpStatus = 0x102,
        QueryStorageInfo = 0x103
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct RadxaPlatformSendCommand
    {
        public uint Cmd;
        public uint NumOfParams;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)MAX_PARAMETERS)]
        public ulong[] Parameters;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = TZ_SECURE_APP_NAME_SIZE)]
        public byte[] AppName;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = SECSVC_BUFFER_SIZE)]
        public byte[] InBuffer;
        public uint InBufferLen;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct RadxaPlatformGetResponse
    {
        public int ErrorVal;
        public ulong Data;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)MAX_RESULTS)]
        public ulong[] Results;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = SECSVC_BUFFER_SIZE)]
        public byte[] OutBuffer;
        public uint OutBufferLen;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct TzStorageInfo
    {
        public ulong TotalBlocks;
        public uint BlockSize;
        public uint PageSize;
        public uint NumPhysical;
        public ulong ManufacturerId;
        public ulong SerialNum;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] FirmwareVersion;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        public byte[] MemoryType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] ProductName;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct QcSpinorRwRequest
    {
        public ulong StartLba;
        public uint SectorCount;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct QcSpinorReadResponseHeader
    {
        public uint BytesPerSector;
        public uint TransferBytes;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public uint[] Reserved;
    }
}