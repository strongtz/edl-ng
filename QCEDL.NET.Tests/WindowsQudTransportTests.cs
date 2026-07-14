using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Qualcomm.EmergencyDownload.Transport;

namespace QCEDL.NET.Tests;

public sealed class WindowsQudTransportTests
{
    [Fact]
    public void ConstructorConfiguresQudHandleWithoutPurgingReceiveQueue()
    {
        var native = new FakeWindowsQudNativeApi { InitialDcbFlags = uint.MaxValue };

        using var transport = CreateTransport(native);

        const uint clearedFlags = (1 << 2) | (1 << 3) | (3 << 4) | (1 << 6) | (1 << 8) |
                                  (1 << 9) | (1 << 11) | (3 << 12) | (1 << 14);
        const uint enabledFlags = (1 << 0) | (1 << 4) | (1 << 12);
        var expectedFlags = (uint.MaxValue & ~clearedFlags) | enabledFlags;
        Assert.Equal(115200U, native.ConfiguredDcb.BaudRate);
        Assert.Equal((byte)8, native.ConfiguredDcb.ByteSize);
        Assert.Equal((byte)0, native.ConfiguredDcb.Parity);
        Assert.Equal((byte)0, native.ConfiguredDcb.StopBits);
        Assert.Equal(expectedFlags, native.ConfiguredDcb.Flags);
        Assert.Equal((64U * 1024, 64U * 1024), native.QueueSizes);
        Assert.Equal(0x0005U, native.PurgeFlags);
        Assert.Equal(uint.MaxValue, native.Timeouts[0].ReadIntervalTimeout);
        Assert.Equal(uint.MaxValue, native.Timeouts[0].ReadTotalTimeoutMultiplier);
        Assert.Equal(1000U, native.Timeouts[0].ReadTotalTimeoutConstant);
    }

    [Fact]
    public void ReadPendingOperationUsesPerCallTimeoutAndReturnsTransferredBytes()
    {
        var native = new FakeWindowsQudNativeApi
        {
            ReadStarted = false,
            ReadError = 997,
            Transferred = 3,
            ReadData = [0x01, 0x02, 0x03]
        };
        using var transport = CreateTransport(native);
        transport.TimeoutMilliseconds = 500;
        var buffer = new byte[8];

        var bytesRead = transport.Read(buffer, 0, buffer.Length);

        Assert.Equal(3, bytesRead);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, buffer[..3]);
        Assert.Equal(750U, native.LastWaitTimeout);
        Assert.Equal(500U, native.Timeouts[^1].ReadTotalTimeoutConstant);
        Assert.Equal(["ReadFile", "Wait", "GetOverlappedResult(False)"], native.IoCalls);
    }

    [Fact]
    public void ReadZeroBytesIsReportedAsTimeout()
    {
        var native = new FakeWindowsQudNativeApi { Transferred = 0 };
        using var transport = CreateTransport(native);

        _ = Assert.Throws<TimeoutException>(() => transport.Read(new byte[8], 0, 8));
    }

    [Fact]
    public void ReadWaitTimeoutCancelsAndDrainsOperation()
    {
        var native = new FakeWindowsQudNativeApi { WaitResult = 258 };
        using var transport = CreateTransport(native);

        _ = Assert.Throws<TimeoutException>(() => transport.Read(new byte[8], 0, 8));

        Assert.Equal(["ReadFile", "Wait", "CancelIoEx", "GetOverlappedResult(True)"], native.IoCalls);
    }

    [Fact]
    public void ReadImmediateNativeFailurePreservesWin32Error()
    {
        var native = new FakeWindowsQudNativeApi { ReadStarted = false, ReadError = 5 };
        using var transport = CreateTransport(native);

        var exception = Assert.Throws<IOException>(() => transport.Read(new byte[8], 0, 8));

        Assert.Equal(5, exception.Data["Win32Error"]);
    }

    [Fact]
    public void WriteImmediateNativeFailurePreservesWin32Error()
    {
        var native = new FakeWindowsQudNativeApi { WriteStarted = false, WriteError = 87 };
        using var transport = CreateTransport(native);

        var exception = Assert.Throws<IOException>(() => transport.Write(new byte[8], 0, 8));

        Assert.Equal(87, exception.Data["Win32Error"]);
    }

    [Fact]
    public void WaitFailurePreservesWin32Error()
    {
        var native = new FakeWindowsQudNativeApi { WaitResult = uint.MaxValue, WaitError = 6 };
        using var transport = CreateTransport(native);

        var exception = Assert.Throws<IOException>(() => transport.Read(new byte[8], 0, 8));

        Assert.Equal(6, exception.Data["Win32Error"]);
    }

    [Fact]
    public void GetOverlappedResultFailurePreservesWin32Error()
    {
        var native = new FakeWindowsQudNativeApi
        {
            GetOverlappedResultSucceeded = false,
            GetOverlappedResultError = 995
        };
        using var transport = CreateTransport(native);

        var exception = Assert.Throws<IOException>(() => transport.Read(new byte[8], 0, 8));

        Assert.Equal(995, exception.Data["Win32Error"]);
    }

    [Fact]
    public void WriteUsesOneOverlappedRequestAndReturnsPartialCount()
    {
        var native = new FakeWindowsQudNativeApi { Transferred = 2 };
        using var transport = CreateTransport(native);
        transport.TimeoutMilliseconds = 1234;

        var bytesWritten = transport.Write([1, 2, 3, 4], 0, 4);

        Assert.Equal(2, bytesWritten);
        Assert.Equal(1234U, native.LastWaitTimeout);
        Assert.Equal(["WriteFile", "Wait", "GetOverlappedResult(False)"], native.IoCalls);
    }

    [Fact]
    public void DisposeIsIdempotentAndRejectsFurtherIo()
    {
        var native = new FakeWindowsQudNativeApi();
        var transport = CreateTransport(native);

        transport.Dispose();
        transport.Dispose();

        _ = Assert.Throws<ObjectDisposedException>(() => transport.Read(new byte[1], 0, 1));
    }

    [Fact]
    public void NativeLayoutsMatchWindowsStructures()
    {
        Assert.Equal(28, Marshal.SizeOf<Dcb>());
        Assert.Equal(20, Marshal.SizeOf<CommTimeouts>());
        Assert.Equal(nint.Size == 8 ? 32 : 20, Marshal.SizeOf<QudOverlapped>());
    }

    [Fact]
    public void PublicConstructorRejectsNonWindowsPlatforms()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        _ = Assert.Throws<PlatformNotSupportedException>(() => new WindowsQudTransport("COM1"));
    }

    private static WindowsQudTransport CreateTransport(FakeWindowsQudNativeApi native)
    {
        return new("\\\\.\\COM5", native, false);
    }
}

internal sealed class FakeWindowsQudNativeApi : IWindowsQudNativeApi
{
    internal uint InitialDcbFlags { get; init; }
    internal Dcb ConfiguredDcb { get; private set; }
    internal List<CommTimeouts> Timeouts { get; } = [];
    internal (uint Input, uint Output) QueueSizes { get; private set; }
    internal uint PurgeFlags { get; private set; }
    internal bool ReadStarted { get; init; } = true;
    internal int ReadError { get; init; }
    internal bool WriteStarted { get; init; } = true;
    internal int WriteError { get; init; }
    internal byte[] ReadData { get; init; } = [];
    internal uint WaitResult { get; init; }
    internal int WaitError { get; init; }
    internal uint Transferred { get; init; } = 1;
    internal bool GetOverlappedResultSucceeded { get; init; } = true;
    internal int GetOverlappedResultError { get; init; }
    internal uint LastWaitTimeout { get; private set; }
    internal List<string> IoCalls { get; } = [];

    public SafeFileHandle CreateFile(string path, uint desiredAccess, uint shareMode, uint creationDisposition,
        uint flagsAndAttributes, out int error)
    {
        error = 0;
        return new(new(1), false);
    }

    public SafeWaitHandle CreateEvent(bool manualReset, bool initialState, out int error)
    {
        error = 0;
        return new(new(2), false);
    }

    public bool GetCommState(SafeFileHandle handle, ref Dcb dcb, out int error)
    {
        dcb.Flags = InitialDcbFlags;
        error = 0;
        return true;
    }

    public bool SetCommState(SafeFileHandle handle, in Dcb dcb, out int error)
    {
        ConfiguredDcb = dcb;
        error = 0;
        return true;
    }

    public bool SetCommTimeouts(SafeFileHandle handle, in CommTimeouts timeouts, out int error)
    {
        Timeouts.Add(timeouts);
        error = 0;
        return true;
    }

    public bool SetupComm(SafeFileHandle handle, uint inputQueueSize, uint outputQueueSize, out int error)
    {
        QueueSizes = (inputQueueSize, outputQueueSize);
        error = 0;
        return true;
    }

    public bool PurgeComm(SafeFileHandle handle, uint flags, out int error)
    {
        PurgeFlags = flags;
        error = 0;
        return true;
    }

    public bool ReadFile(SafeFileHandle handle, nint buffer, uint count, nint overlapped, out int error)
    {
        IoCalls.Add("ReadFile");
        if (ReadData.Length > 0)
        {
            Marshal.Copy(ReadData, 0, buffer, Math.Min(ReadData.Length, checked((int)count)));
        }

        error = ReadError;
        return ReadStarted;
    }

    public bool WriteFile(SafeFileHandle handle, nint buffer, uint count, nint overlapped, out int error)
    {
        IoCalls.Add("WriteFile");
        error = WriteError;
        return WriteStarted;
    }

    public uint WaitForSingleObject(SafeWaitHandle handle, uint timeoutMilliseconds, out int error)
    {
        IoCalls.Add("Wait");
        LastWaitTimeout = timeoutMilliseconds;
        error = WaitError;
        return WaitResult;
    }

    public bool CancelIoEx(SafeFileHandle handle, nint overlapped, out int error)
    {
        IoCalls.Add("CancelIoEx");
        error = 0;
        return true;
    }

    public bool GetOverlappedResult(SafeFileHandle handle, nint overlapped, bool wait, out uint transferred,
        out int error)
    {
        IoCalls.Add($"GetOverlappedResult({wait})");
        transferred = Transferred;
        error = GetOverlappedResultError;
        return GetOverlappedResultSucceeded;
    }
}