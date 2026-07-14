using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using LibUsbDotNet;
using LibUsbDotNet.LibUsb;
using LibUsbDotNet.Main;
using QCEDL.NET.Logging;
using LogLevel = LibUsbDotNet.LogLevel;

namespace Qualcomm.EmergencyDownload.Transport;

public sealed class LibUsbTransport : IQualcommTransport
{
    private static readonly Regex VidRegex = new(@"VID_([0-9A-Fa-f]{4})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PidRegex = new(@"PID_([0-9A-Fa-f]{4})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private bool _disposed;
    private UsbDevice? _device;
    private UsbEndpointReader? _reader;
    private UsbEndpointWriter? _writer;
    private int _timeoutMilliseconds = 1000;

    public static UsbContext? Context { get; }

    public TransportBackend Backend => TransportBackend.LibUsb;

    public int TimeoutMilliseconds
    {
        get => _timeoutMilliseconds;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _timeoutMilliseconds = value;
        }
    }

    static LibUsbTransport()
    {
        try
        {
            Context = new();
            Context.SetDebugLevel(LogLevel.Warning);
        }
        catch (Exception ex)
        {
            LibraryLogger.Error($"Failed to initialize LibUsbDotNet context: {ex.Message}");
        }
    }

    public LibUsbTransport(string deviceIdOrPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceIdOrPath);

        try
        {
            var (vid, pid) = ExtractVidPid(deviceIdOrPath);
            LibraryLogger.Debug($"Searching LibUsb for VID=0x{vid:X4}, PID=0x{pid:X4}");
            var finder = new UsbDeviceFinder { Vid = vid, Pid = pid };
            _device = Context?.Find(finder) as UsbDevice
                ?? throw new IOException($"LibUsb device VID=0x{vid:X4}, PID=0x{pid:X4} was not found.");

            _device.Open();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _device.SetConfiguration(1);
            }

            const int interfaceNumber = 0;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && _device.SupportsDetachKernelDriver() &&
                _device.IsKernelDriverActive(interfaceNumber))
            {
                _device.DetachKernelDriver(interfaceNumber);
            }

            _ = _device.ClaimInterface(interfaceNumber);
            _reader = _device.OpenEndpointReader(ReadEndpointID.Ep01);
            _writer = _device.OpenEndpointWriter(WriteEndpointID.Ep01);
            if (_reader is null || _writer is null)
            {
                throw new IOException("LibUsb could not open the required bulk endpoints.");
            }

            LibraryLogger.Debug($"Using LibUsb backend for VID=0x{vid:X4}, PID=0x{pid:X4}");
        }
        catch
        {
            try
            {
                Dispose();
            }
            catch (Exception cleanupException)
            {
                LibraryLogger.Warning($"Failed to clean up LibUsb after initialization error: {cleanupException.Message}");
            }

            throw;
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateBuffer(buffer, offset, count);

        var target = offset == 0 && count == buffer.Length ? buffer : new byte[count];
        var error = _reader!.Read(target, EffectiveTimeout, out var bytesRead);
        if (error == Error.Success && bytesRead == 0)
        {
            error = _reader.Read(target, EffectiveTimeout, out bytesRead);
        }

        ThrowForError(error, "read");
        if (!ReferenceEquals(target, buffer) && bytesRead > 0)
        {
            Buffer.BlockCopy(target, 0, buffer, offset, bytesRead);
        }

        return bytesRead;
    }

    public int Write(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateBuffer(buffer, offset, count);

        var source = offset == 0 && count == buffer.Length
            ? buffer
            : buffer.AsSpan(offset, count).ToArray();
        var error = _writer!.Write(source, EffectiveTimeout, out var bytesWritten);
        ThrowForError(error, "write");
        return bytesWritten;
    }

    public void SendZeroLengthPacket()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var error = _writer!.Write([], EffectiveTimeout, out var bytesWritten);
        ThrowForError(error, "zero-length write");
        if (bytesWritten != 0)
        {
            throw new IOException($"LibUsb zero-length write unexpectedly reported {bytesWritten} bytes.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (_device is not null)
            {
                try
                {
                    _ = _device.ReleaseInterface(0);
                }
                catch (Exception ex)
                {
                    LibraryLogger.Warning($"Failed to release LibUsb interface: {ex.Message}");
                }

                _device.Close();
            }
        }
        finally
        {
            _reader = null;
            _writer = null;
            _device = null;
            _disposed = true;
        }
    }

    private int EffectiveTimeout => Math.Max(TimeoutMilliseconds, 1);

    private static (int Vid, int Pid) ExtractVidPid(string devicePath)
    {
        var vidMatch = VidRegex.Match(devicePath);
        var pidMatch = PidRegex.Match(devicePath);
        return !vidMatch.Success || !pidMatch.Success ||
            !int.TryParse(vidMatch.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                out var vid) ||
            !int.TryParse(pidMatch.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                out var pid)
            ? throw new ArgumentException("Could not extract a valid VID/PID from the device path.",
                nameof(devicePath))
            : ((int Vid, int Pid))(vid, pid);
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
    }

    private static void ThrowForError(Error error, string operation)
    {
        if (error == Error.Success)
        {
            return;
        }

        if (error == Error.Timeout)
        {
            throw new TimeoutException($"LibUsb {operation} timed out.");
        }

        var exception = new IOException($"LibUsb {operation} failed with error {error}.");
        exception.Data[nameof(Error)] = error;
        throw exception;
    }
}