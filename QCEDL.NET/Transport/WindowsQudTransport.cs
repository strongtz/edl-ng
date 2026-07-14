using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Qualcomm.EmergencyDownload.Transport;

public sealed class WindowsQudTransport : IQualcommTransport
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;
    private const uint ErrorIoPending = 997;
    private const uint WaitObject0 = 0;
    private const uint WaitTimeout = 258;
    private const uint MaxDword = uint.MaxValue;
    private const uint PurgeTxAbort = 0x0001;
    private const uint PurgeTxClear = 0x0004;
    private const uint DcbBinary = 1 << 0;
    private const uint DcbOutxCtsFlow = 1 << 2;
    private const uint DcbOutxDsrFlow = 1 << 3;
    private const uint DcbDtrControlMask = 3 << 4;
    private const uint DcbDtrControlEnable = 1 << 4;
    private const uint DcbDsrSensitivity = 1 << 6;
    private const uint DcbOutX = 1 << 8;
    private const uint DcbInX = 1 << 9;
    private const uint DcbNull = 1 << 11;
    private const uint DcbRtsControlMask = 3 << 12;
    private const uint DcbRtsControlEnable = 1 << 12;
    private const uint DcbAbortOnError = 1 << 14;
    private const byte NoParity = 0;
    private const byte OneStopBit = 0;

    private readonly IWindowsQudNativeApi _nativeApi;
    private readonly string _path;
    private SafeFileHandle? _handle;
    private int _timeoutMilliseconds = 1000;
    private bool _disposed;

    public TransportBackend Backend => TransportBackend.WindowsQud;

    public int TimeoutMilliseconds
    {
        get => _timeoutMilliseconds;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _timeoutMilliseconds = value;
        }
    }

    public WindowsQudTransport(string path)
        : this(path, WindowsQudNativeApi.Instance, true)
    {
    }

    internal WindowsQudTransport(string path, IWindowsQudNativeApi nativeApi, bool enforcePlatform)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(nativeApi);
        if (enforcePlatform && !OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The QUD transport is only supported on Windows.");
        }

        _path = path;
        _nativeApi = nativeApi;
        _handle = _nativeApi.CreateFile(path, GenericRead | GenericWrite, 0, OpenExisting,
            FileFlagOverlapped, out var error);
        if (_handle.IsInvalid)
        {
            _handle.Dispose();
            _handle = null;
            throw CreateIOException($"CreateFile({path})", error);
        }

        try
        {
            ConfigureHandle();
        }
        catch
        {
            _handle.Dispose();
            _handle = null;
            throw;
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        ThrowIfDisposed();
        ValidateBuffer(buffer, offset, count);

        var timeout = new CommTimeouts
        {
            ReadIntervalTimeout = MaxDword,
            ReadTotalTimeoutMultiplier = MaxDword,
            ReadTotalTimeoutConstant = (uint)Math.Max(TimeoutMilliseconds, 1),
            WriteTotalTimeoutMultiplier = 0,
            WriteTotalTimeoutConstant = 0
        };
        if (!_nativeApi.SetCommTimeouts(_handle!, in timeout, out var error))
        {
            throw CreateIOException("SetCommTimeouts", error);
        }

        var bytesRead = OverlappedIo(buffer, offset, count, (uint)TimeoutMilliseconds + 250, false);
        return bytesRead == 0 ? throw new TimeoutException($"QUD read from {_path} timed out.") : bytesRead;
    }

    public int Write(byte[] buffer, int offset, int count)
    {
        ThrowIfDisposed();
        ValidateBuffer(buffer, offset, count);
        return OverlappedIo(buffer, offset, count, (uint)TimeoutMilliseconds, true);
    }

    public void SendZeroLengthPacket()
    {
        ThrowIfDisposed();
        // The QDLoader kernel driver owns USB transfer framing; a user-mode ZLP is not applicable.
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _handle?.Dispose();
        _handle = null;
        _disposed = true;
    }

    private void ConfigureHandle()
    {
        var dcb = new Dcb { Length = (uint)Marshal.SizeOf<Dcb>() };
        if (!_nativeApi.GetCommState(_handle!, ref dcb, out var error))
        {
            throw CreateIOException("GetCommState", error);
        }

        dcb.BaudRate = 115200;
        dcb.ByteSize = 8;
        dcb.Parity = NoParity;
        dcb.StopBits = OneStopBit;
        dcb.Flags |= DcbBinary;
        dcb.Flags &= ~(DcbOutxCtsFlow | DcbOutxDsrFlow | DcbDsrSensitivity | DcbOutX | DcbInX |
                       DcbNull | DcbAbortOnError | DcbDtrControlMask | DcbRtsControlMask);
        dcb.Flags |= DcbDtrControlEnable | DcbRtsControlEnable;
        if (!_nativeApi.SetCommState(_handle!, in dcb, out error))
        {
            throw CreateIOException("SetCommState", error);
        }

        var timeout = new CommTimeouts
        {
            ReadIntervalTimeout = MaxDword,
            ReadTotalTimeoutMultiplier = MaxDword,
            ReadTotalTimeoutConstant = 1000,
            WriteTotalTimeoutMultiplier = 0,
            WriteTotalTimeoutConstant = 0
        };
        if (!_nativeApi.SetCommTimeouts(_handle!, in timeout, out error))
        {
            throw CreateIOException("SetCommTimeouts", error);
        }

        _ = _nativeApi.SetupComm(_handle!, 64 * 1024, 64 * 1024, out _);
        _ = _nativeApi.PurgeComm(_handle!, PurgeTxClear | PurgeTxAbort, out _);
    }

    private unsafe int OverlappedIo(byte[] buffer, int offset, int count, uint timeoutMilliseconds,
        bool isWrite)
    {
        using var waitHandle = _nativeApi.CreateEvent(true, false, out var error);
        if (waitHandle.IsInvalid)
        {
            throw CreateIOException("CreateEvent", error);
        }

        var overlapped = new QudOverlapped { EventHandle = waitHandle.DangerousGetHandle() };
        fixed (byte* bufferPointer = &buffer[offset])
        {
            var overlappedPointer = (nint)(&overlapped);
            var operationStarted = isWrite
                ? _nativeApi.WriteFile(_handle!, (nint)bufferPointer, (uint)count, overlappedPointer,
                    out error)
                : _nativeApi.ReadFile(_handle!, (nint)bufferPointer, (uint)count, overlappedPointer,
                    out error);
            if (!operationStarted && error != ErrorIoPending)
            {
                throw CreateIOException(isWrite ? "WriteFile" : "ReadFile", error);
            }

            var waitResult = _nativeApi.WaitForSingleObject(waitHandle, timeoutMilliseconds, out error);
            if (waitResult == WaitTimeout)
            {
                _ = _nativeApi.CancelIoEx(_handle!, overlappedPointer, out _);
                _ = _nativeApi.GetOverlappedResult(_handle!, overlappedPointer, true, out _, out _);
                throw new TimeoutException($"QUD {(isWrite ? "write to" : "read from")} {_path} timed out.");
            }

            return waitResult != WaitObject0
                ? throw CreateIOException("WaitForSingleObject", error)
                : !_nativeApi.GetOverlappedResult(_handle!, overlappedPointer, false, out var transferred,
                    out error)
                ? throw CreateIOException("GetOverlappedResult", error)
                : checked((int)transferred);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static void ValidateBuffer(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count)
        {
            throw new ArgumentException("The offset and count exceed the buffer length.");
        }

        if (count == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "QUD reads and writes require a non-empty buffer.");
        }
    }

    private static IOException CreateIOException(string operation, int error)
    {
        var exception = new IOException($"{operation} failed with Win32 error {error}.",
            new Win32Exception(error));
        exception.Data["Win32Error"] = error;
        return exception;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct CommTimeouts
{
    internal uint ReadIntervalTimeout;
    internal uint ReadTotalTimeoutMultiplier;
    internal uint ReadTotalTimeoutConstant;
    internal uint WriteTotalTimeoutMultiplier;
    internal uint WriteTotalTimeoutConstant;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Dcb
{
    internal uint Length;
    internal uint BaudRate;
    internal uint Flags;
    internal ushort Reserved;
    internal ushort XonLimit;
    internal ushort XoffLimit;
    internal byte ByteSize;
    internal byte Parity;
    internal byte StopBits;
    internal sbyte XonChar;
    internal sbyte XoffChar;
    internal sbyte ErrorChar;
    internal sbyte EofChar;
    internal sbyte EventChar;
    internal ushort Reserved1;
}

[StructLayout(LayoutKind.Sequential)]
internal struct QudOverlapped
{
    internal nuint Internal;
    internal nuint InternalHigh;
    internal uint Offset;
    internal uint OffsetHigh;
    internal nint EventHandle;
}

internal interface IWindowsQudNativeApi
{
    SafeFileHandle CreateFile(string path, uint desiredAccess, uint shareMode, uint creationDisposition,
        uint flagsAndAttributes, out int error);

    SafeWaitHandle CreateEvent(bool manualReset, bool initialState, out int error);

    bool GetCommState(SafeFileHandle handle, ref Dcb dcb, out int error);

    bool SetCommState(SafeFileHandle handle, in Dcb dcb, out int error);

    bool SetCommTimeouts(SafeFileHandle handle, in CommTimeouts timeouts, out int error);

    bool SetupComm(SafeFileHandle handle, uint inputQueueSize, uint outputQueueSize, out int error);

    bool PurgeComm(SafeFileHandle handle, uint flags, out int error);

    bool ReadFile(SafeFileHandle handle, nint buffer, uint count, nint overlapped, out int error);

    bool WriteFile(SafeFileHandle handle, nint buffer, uint count, nint overlapped, out int error);

    uint WaitForSingleObject(SafeWaitHandle handle, uint timeoutMilliseconds, out int error);

    bool CancelIoEx(SafeFileHandle handle, nint overlapped, out int error);

    bool GetOverlappedResult(SafeFileHandle handle, nint overlapped, bool wait, out uint transferred,
        out int error);
}

internal sealed class WindowsQudNativeApi : IWindowsQudNativeApi
{
    internal static WindowsQudNativeApi Instance { get; } = new();

    private WindowsQudNativeApi()
    {
    }

    public SafeFileHandle CreateFile(string path, uint desiredAccess, uint shareMode, uint creationDisposition,
        uint flagsAndAttributes, out int error)
    {
        var handle = CreateFileW(path, desiredAccess, shareMode, nint.Zero, creationDisposition,
            flagsAndAttributes, nint.Zero);
        error = Marshal.GetLastWin32Error();
        return handle;
    }

    public SafeWaitHandle CreateEvent(bool manualReset, bool initialState, out int error)
    {
        var handle = CreateEventW(nint.Zero, manualReset, initialState, null);
        error = Marshal.GetLastWin32Error();
        return handle;
    }

    public bool GetCommState(SafeFileHandle handle, ref Dcb dcb, out int error)
    {
        var result = GetCommStateNative(handle, ref dcb);
        error = Marshal.GetLastWin32Error();
        return result;
    }

    public bool SetCommState(SafeFileHandle handle, in Dcb dcb, out int error)
    {
        var result = SetCommStateNative(handle, in dcb);
        error = Marshal.GetLastWin32Error();
        return result;
    }

    public bool SetCommTimeouts(SafeFileHandle handle, in CommTimeouts timeouts, out int error)
    {
        var result = SetCommTimeoutsNative(handle, in timeouts);
        error = Marshal.GetLastWin32Error();
        return result;
    }

    public bool SetupComm(SafeFileHandle handle, uint inputQueueSize, uint outputQueueSize, out int error)
    {
        var result = SetupCommNative(handle, inputQueueSize, outputQueueSize);
        error = Marshal.GetLastWin32Error();
        return result;
    }

    public bool PurgeComm(SafeFileHandle handle, uint flags, out int error)
    {
        var result = PurgeCommNative(handle, flags);
        error = Marshal.GetLastWin32Error();
        return result;
    }

    public bool ReadFile(SafeFileHandle handle, nint buffer, uint count, nint overlapped, out int error)
    {
        var result = ReadFileNative(handle, buffer, count, nint.Zero, overlapped);
        error = Marshal.GetLastWin32Error();
        return result;
    }

    public bool WriteFile(SafeFileHandle handle, nint buffer, uint count, nint overlapped, out int error)
    {
        var result = WriteFileNative(handle, buffer, count, nint.Zero, overlapped);
        error = Marshal.GetLastWin32Error();
        return result;
    }

    public uint WaitForSingleObject(SafeWaitHandle handle, uint timeoutMilliseconds, out int error)
    {
        var result = WaitForSingleObjectNative(handle, timeoutMilliseconds);
        error = Marshal.GetLastWin32Error();
        return result;
    }

    public bool CancelIoEx(SafeFileHandle handle, nint overlapped, out int error)
    {
        var result = CancelIoExNative(handle, overlapped);
        error = Marshal.GetLastWin32Error();
        return result;
    }

    public bool GetOverlappedResult(SafeFileHandle handle, nint overlapped, bool wait, out uint transferred,
        out int error)
    {
        var result = GetOverlappedResultNative(handle, overlapped, out transferred, wait);
        error = Marshal.GetLastWin32Error();
        return result;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode,
        nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeWaitHandle CreateEventW(nint eventAttributes, [MarshalAs(UnmanagedType.Bool)] bool manualReset,
        [MarshalAs(UnmanagedType.Bool)] bool initialState, string? name);

    [DllImport("kernel32.dll", EntryPoint = "GetCommState", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCommStateNative(SafeFileHandle handle, ref Dcb dcb);

    [DllImport("kernel32.dll", EntryPoint = "SetCommState", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCommStateNative(SafeFileHandle handle, in Dcb dcb);

    [DllImport("kernel32.dll", EntryPoint = "SetCommTimeouts", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCommTimeoutsNative(SafeFileHandle handle, in CommTimeouts timeouts);

    [DllImport("kernel32.dll", EntryPoint = "SetupComm", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupCommNative(SafeFileHandle handle, uint inputQueueSize, uint outputQueueSize);

    [DllImport("kernel32.dll", EntryPoint = "PurgeComm", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PurgeCommNative(SafeFileHandle handle, uint flags);

    [DllImport("kernel32.dll", EntryPoint = "ReadFile", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFileNative(SafeFileHandle handle, nint buffer, uint bytesToRead,
        nint bytesRead, nint overlapped);

    [DllImport("kernel32.dll", EntryPoint = "WriteFile", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFileNative(SafeFileHandle handle, nint buffer, uint bytesToWrite,
        nint bytesWritten, nint overlapped);

    [DllImport("kernel32.dll", EntryPoint = "WaitForSingleObject", SetLastError = true)]
    private static extern uint WaitForSingleObjectNative(SafeWaitHandle handle, uint milliseconds);

    [DllImport("kernel32.dll", EntryPoint = "CancelIoEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CancelIoExNative(SafeFileHandle handle, nint overlapped);

    [DllImport("kernel32.dll", EntryPoint = "GetOverlappedResult", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOverlappedResultNative(SafeFileHandle handle, nint overlapped,
        out uint bytesTransferred, [MarshalAs(UnmanagedType.Bool)] bool wait);
}