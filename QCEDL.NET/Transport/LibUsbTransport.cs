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
    private readonly int _claimedInterface;
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

            LogDeviceDescriptors();
            var (interfaceNumber, readEndpoint, writeEndpoint) = SelectBulkEndpoints();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && _device.SupportsDetachKernelDriver() &&
                _device.IsKernelDriverActive(interfaceNumber))
            {
                _device.DetachKernelDriver(interfaceNumber);
            }

            _ = _device.ClaimInterface(interfaceNumber);
            _claimedInterface = interfaceNumber;
            _reader = _device.OpenEndpointReader(readEndpoint);
            _writer = _device.OpenEndpointWriter(writeEndpoint);
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
                    _ = _device.ReleaseInterface(_claimedInterface);
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

    private void LogDeviceDescriptors()
    {
        if (_device is null)
        {
            return;
        }

        try
        {
            var info = _device.Info;
            LibraryLogger.Debug(
                $"USB descriptors: VID={info.VendorId:X4} PID={info.ProductId:X4}, bcdUSB 0x{info.Usb:X4}, bcdDevice 0x{info.Device:X4}, class {info.DeviceClass}/{info.DeviceSubClass}/{info.DeviceProtocol}, {info.NumConfigurations} configuration(s).");
            LibraryLogger.Debug(
                $"USB strings: manufacturer \"{info.Manufacturer}\", product \"{info.Product}\", serial \"{info.SerialNumber}\".");
            foreach (var configuration in info.Configurations)
            {
                LibraryLogger.Debug(
                    $"  Configuration {configuration.ConfigurationValue}: {configuration.Interfaces.Count} interface(s).");
                foreach (var usbInterface in configuration.Interfaces)
                {
                    LibraryLogger.Debug(
                        $"    Interface {usbInterface.Number} alt {usbInterface.AlternateSetting}: class {usbInterface.Class}/{usbInterface.SubClass}/{usbInterface.Protocol}, {usbInterface.Endpoints.Count} endpoint(s).");
                    foreach (var endpoint in usbInterface.Endpoints)
                    {
                        LibraryLogger.Debug(
                            $"      Endpoint 0x{endpoint.EndpointAddress:X2} {((endpoint.EndpointAddress & 0x80) != 0 ? "IN " : "OUT")} {DescribeEndpointType(endpoint.Attributes)}, max packet {endpoint.MaxPacketSize}.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LibraryLogger.Debug($"Could not read the USB descriptors: {ex.Message}");
        }
    }

    private (int InterfaceNumber, ReadEndpointID ReadEndpoint, WriteEndpointID WriteEndpoint) SelectBulkEndpoints()
    {
        const int defaultInterface = 0;
        try
        {
            foreach (var configuration in _device!.Info.Configurations)
            {
                foreach (var usbInterface in configuration.Interfaces)
                {
                    byte? bulkIn = null;
                    byte? bulkOut = null;
                    foreach (var endpoint in usbInterface.Endpoints)
                    {
                        if ((endpoint.Attributes & 0x03) != 0x02)
                        {
                            continue;
                        }

                        if ((endpoint.EndpointAddress & 0x80) != 0)
                        {
                            bulkIn ??= endpoint.EndpointAddress;
                        }
                        else
                        {
                            bulkOut ??= endpoint.EndpointAddress;
                        }
                    }

                    if (bulkIn is null || bulkOut is null)
                    {
                        continue;
                    }

                    if (usbInterface.Number != defaultInterface ||
                        bulkIn.Value != (byte)ReadEndpointID.Ep01 ||
                        bulkOut.Value != (byte)WriteEndpointID.Ep01)
                    {
                        LibraryLogger.Warning(
                            $"Using interface {usbInterface.Number}, bulk IN 0x{bulkIn.Value:X2} / OUT 0x{bulkOut.Value:X2}.");
                    }

                    return (usbInterface.Number, (ReadEndpointID)bulkIn.Value, (WriteEndpointID)bulkOut.Value);
                }
            }

            LibraryLogger.Warning(
                "No bulk endpoint pair was found in the USB descriptors; falling back to interface 0 endpoint 1.");
        }
        catch (Exception ex)
        {
            LibraryLogger.Debug($"Could not inspect the USB endpoints: {ex.Message}");
        }

        return (defaultInterface, ReadEndpointID.Ep01, WriteEndpointID.Ep01);
    }

    private static string DescribeEndpointType(byte attributes)
    {
        return (attributes & 0x03) switch
        {
            0x00 => "control",
            0x01 => "isochronous",
            0x02 => "bulk",
            _ => "interrupt"
        };
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
