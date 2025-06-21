// Copyright (c) 2018, Rene Lergner - @Heathcliff74xda
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System.Globalization;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using LibUsbDotNet;
using LibUsbDotNet.LibUsb;
using LibUsbDotNet.Main;
using Microsoft.Win32;
using QCEDL.NET.Extensions;
using QCEDL.NET.Logging;
using QCEDL.NET.Todo;
using LogLevel = LibUsbDotNet.LogLevel;

namespace Qualcomm.EmergencyDownload.Transport;

public enum CommunicationMode
{
    None,
    SerialPort,
    LibUsbDotNet
}

public class QualcommSerial : IDisposable
{
    private bool _disposed;
    private readonly SerialPort? _port;

    public static UsbContext? LibUsbContext { get; private set; }
    private UsbDevice? _libUsbDevice;
    private UsbEndpointReader? _libUsbReader;
    private UsbEndpointWriter? _libUsbWriter;
    private readonly CommunicationMode _mode = CommunicationMode.None;

    private int _libUsbTimeoutMs = 1000;

    public CommunicationMode ActiveCommunicationMode { get; private set; } = CommunicationMode.None;

    // Static constructor for LibUsb context
    static QualcommSerial()
    {
        try
        {
            LibUsbContext = new();
            LibUsbContext.SetDebugLevel(LogLevel.Warning);
        }
        catch (Exception ex)
        {
            LibraryLogger.Error($"Failed to initialize LibUsbDotNet context: {ex.Message}");
            LibUsbContext = null;
        }
    }

    public QualcommSerial(string deviceIdOrPath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (deviceIdOrPath.StartsWithOrdinal("/dev/tty"))
            {
                _port = new(deviceIdOrPath, 115200) { ReadTimeout = 1000, WriteTimeout = 1000, };
                if (_port != null)
                {
                    try
                    {
                        var desiredBufferSize = 1024 * 1024;
                        _port.WriteBufferSize = desiredBufferSize;
                        LibraryLogger.Debug(
                            $"SerialPort: Attempted to set WriteBufferSize to {desiredBufferSize}. Actual: {_port.WriteBufferSize}");
                        _port.Open();
                    }
                    catch (Exception ex)
                    {
                        LibraryLogger.Warning($"SerialPort: Failed to set WriteBufferSize. Error: {ex.Message}");
                    }
                }

                _mode = CommunicationMode.SerialPort;
                ActiveCommunicationMode = _mode;
                LibraryLogger.Debug(
                    $"Using System.IO.Ports for {deviceIdOrPath} on Linux. Timeout: {_libUsbTimeoutMs}ms");
            }
            else
            {
                LibraryLogger.Debug($"Attempting LibUsbDotNet backend for {deviceIdOrPath}...");
                try
                {
                    (var vid, var pid) = ExtractVidPidFromDevicePath(deviceIdOrPath);
                    if (vid == 0 || pid == 0)
                    {
                        throw new TodoException("Could not extract VID/PID from device path for LibUsbDotNet.");
                    }

                    LibraryLogger.Debug($"Searching LibUsb for VID=0x{vid:X4}, PID=0x{pid:X4}");
                    var finder = new UsbDeviceFinder { Vid = vid, Pid = pid };

                    _libUsbDevice = LibUsbContext?.Find(finder) as UsbDevice;
                    if (_libUsbDevice == null)
                    {
                        throw new TodoException(
                            $"LibUsbDotNet: Device with VID=0x{vid:X4}, PID=0x{pid:X4} not found.");
                    }

                    _libUsbDevice.Open();

                    // seems to be unnecessary
                    // _libUsbDevice.SetConfiguration(1);

                    var interfaceNumberToClaim = 0;
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        if (_libUsbDevice.SupportsDetachKernelDriver())
                        {
                            LibraryLogger.Debug("Platform supports kernel driver detachment.");
                            if (_libUsbDevice.IsKernelDriverActive(interfaceNumberToClaim))
                            {
                                LibraryLogger.Info(
                                    $"Kernel driver is active on interface {interfaceNumberToClaim}, attempting manual detach...");
                                try
                                {
                                    _libUsbDevice.DetachKernelDriver(interfaceNumberToClaim);
                                    LibraryLogger.Info("Manual detach successful.");
                                }
                                catch (UsbException ue)
                                {
                                    LibraryLogger.Error($"Manual detach failed: {ue.Message}");
                                }
                            }
                        }
                        else
                        {
                            LibraryLogger.Info(
                                "Platform does not support kernel driver detachment or libusb cannot determine support. Proceeding...");
                        }
                    }

                    _ = _libUsbDevice.ClaimInterface(0);

                    _libUsbReader = _libUsbDevice.OpenEndpointReader(ReadEndpointID.Ep01);
                    _libUsbWriter = _libUsbDevice.OpenEndpointWriter(WriteEndpointID.Ep01);
                    if (_libUsbReader == null || _libUsbWriter == null)
                    {
                        throw new TodoException("LibUsbDotNet: Could not open required bulk IN/OUT endpoints.");
                    }

                    _mode = CommunicationMode.LibUsbDotNet;
                    ActiveCommunicationMode = _mode;
                    LibraryLogger.Debug($"Using LibUsbDotNet backend for VID=0x{vid:X4}, PID=0x{pid:X4}");
                }
                catch (Exception ex)
                {
                    LibraryLogger.Error($"Failed to initialize LibUsbDotNet backend: {ex.Message}");
                    _libUsbDevice?.Close();
                    _libUsbDevice = null;
                    _libUsbReader = null;
                    _libUsbWriter = null;
                    _mode = CommunicationMode.None;
                    ActiveCommunicationMode = _mode;
                }
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var devicePathElements = deviceIdOrPath.Split(['#']);
            if (string.Equals(devicePathElements[3], "{86E0D1E0-8089-11D0-9CE4-08003E301F73}",
                    StringComparison.OrdinalIgnoreCase))
            {
                var portName = (string?)Registry.GetValue(
                    $@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Enum\USB\{devicePathElements[1]}\{devicePathElements[2]}\Device Parameters",
                    "PortName", null);
                if (portName != null)
                {
                    try
                    {
                        _port = new(portName, 115200) { ReadTimeout = 1000, WriteTimeout = 1000 };
                        if (_port != null)
                        {
                            try
                            {
                                var desiredBufferSize = 1024 * 1024;
                                _port.ReadBufferSize = desiredBufferSize;
                                _port.WriteBufferSize = desiredBufferSize;
                                LibraryLogger.Debug(
                                    $"SerialPort: Attempted ReadBufferSize={desiredBufferSize}. Actual: {_port.ReadBufferSize}");
                                LibraryLogger.Debug(
                                    $"SerialPort: Attempted to set WriteBufferSize to {desiredBufferSize}. Actual: {_port.WriteBufferSize}");
                            }
                            catch (Exception ex)
                            {
                                LibraryLogger.Warning(
                                    $"SerialPort: Failed to set WriteBufferSize. Error: {ex.Message}");
                            }

                            _port.Open();
                        }

                        _mode = CommunicationMode.SerialPort;
                        ActiveCommunicationMode = _mode;
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        LibraryLogger.Error($"Failed to open SerialPort: {ex.Message}");
                        LibraryLogger.Error(
                            "Please check if the port is already in use by some Qualcomm software");
                        LibraryLogger.Error(
                            "Try stopping the 'Qualcomm Unified Tools Service' if you have closed every other suspicious program");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        LibraryLogger.Error($"Failed to open SerialPort: {ex}");
                        throw new IOException($"Failed to open SerialPort: {ex.Message}", ex);
                    }
                }
            }
            else
            {
                LibraryLogger.Debug($"Attempting LibUsbDotNet backend for {deviceIdOrPath}...");
                try
                {
                    // LibUsbDotNet doesn't use the Windows device path directly.
                    // We need to find the device using VID/PID extracted from the path.
                    (var vid, var pid) = ExtractVidPidFromDevicePath(deviceIdOrPath);
                    if (vid == 0 || pid == 0)
                    {
                        throw new TodoException("Could not extract VID/PID from device path for LibUsbDotNet.");
                    }

                    LibraryLogger.Debug($"Searching LibUsb for VID=0x{vid:X4}, PID=0x{pid:X4}");
                    var finder = new UsbDeviceFinder
                    {
                        Vid = vid,
                        Pid = pid
                        // You can also set SerialNumber here if needed:
                        // SerialNumber = "YourSerialNumber"
                    };
                    _libUsbDevice = LibUsbContext?.Find(finder) as UsbDevice;
                    if (_libUsbDevice == null)
                    {
                        throw new TodoException(
                            $"LibUsbDotNet: Device with VID=0x{vid:X4}, PID=0x{pid:X4} not found.");
                    }

                    _libUsbDevice.Open();

                    _libUsbDevice.SetConfiguration(1);
                    _ = _libUsbDevice.ClaimInterface(0);

                    _libUsbReader = _libUsbDevice.OpenEndpointReader(ReadEndpointID.Ep01);
                    _libUsbWriter = _libUsbDevice.OpenEndpointWriter(WriteEndpointID.Ep01);
                    if (_libUsbReader == null || _libUsbWriter == null)
                    {
                        throw new TodoException("LibUsbDotNet: Could not open required bulk IN/OUT endpoints.");
                    }

                    _mode = CommunicationMode.LibUsbDotNet;
                    ActiveCommunicationMode = _mode;
                    LibraryLogger.Debug($"Using LibUsbDotNet backend for VID=0x{vid:X4}, PID=0x{pid:X4}");
                }
                catch (Exception ex)
                {
                    LibraryLogger.Error($"Failed to initialize LibUsbDotNet backend: {ex.Message}");
                    _libUsbDevice?.Close();
                    _libUsbDevice = null;
                    _libUsbReader = null;
                    _libUsbWriter = null;
                    _mode = CommunicationMode.None;
                    ActiveCommunicationMode = _mode;
                }
            }
        }
        else
        {
            _mode = CommunicationMode.None;
            ActiveCommunicationMode = _mode;
            throw new PlatformNotSupportedException($"Unsupported OS: {RuntimeInformation.OSDescription}");
        }
    }

    private static (int vid, int pid) ExtractVidPidFromDevicePath(string devicePath)
    {
        var vid = 0;
        var pid = 0;
        try
        {
            var matchVid = Regex.Match(devicePath, @"VID_([0-9A-Fa-f]{4})",
                RegexOptions.IgnoreCase);
            var matchPid = Regex.Match(devicePath, @"PID_([0-9A-Fa-f]{4})",
                RegexOptions.IgnoreCase);
            if (matchVid.Success)
            {
                _ = int.TryParse(matchVid.Groups[1].Value, NumberStyles.HexNumber, null, out vid);
            }

            if (matchPid.Success)
            {
                _ = int.TryParse(matchPid.Groups[1].Value, NumberStyles.HexNumber, null, out pid);
            }
        }
        catch
        {
            /* Ignore parsing errors */
        }

        return (vid, pid);
    }

    // Method for sending large raw data (e.g., for Firehose program) with internal chunking for SerialPort
    public void SendLargeRawData(byte[] largeData)
    {
        if (_port != null)
        {
            var bytesWritten = 0;
            var totalBytes = largeData.Length;
            var chunkSize = _port.WriteBufferSize > 0 ? _port.WriteBufferSize : 2048; // Default to 2KB if not set
            if (chunkSize <= 0)
            {
                chunkSize = 4096;
            }

            LibraryLogger.Trace(
                $"SerialPort (LargeRaw): Sending {totalBytes} bytes in chunks of {chunkSize}. Timeout: {_port.WriteTimeout}ms");
            while (bytesWritten < totalBytes)
            {
                var bytesToWriteThisChunk = Math.Min(chunkSize, totalBytes - bytesWritten);
                try
                {
                    _port.Write(largeData, bytesWritten, bytesToWriteThisChunk);
                    bytesWritten += bytesToWriteThisChunk;
                    LibraryLogger.Trace(
                        $"SerialPort (LargeRaw): Wrote chunk of {bytesToWriteThisChunk} bytes. Total written: {bytesWritten}/{totalBytes}");
                }
                catch (TimeoutException ex)
                {
                    LibraryLogger.Error(
                        $"SerialPort (LargeRaw) Write Timeout: Wrote {bytesWritten}/{totalBytes} bytes. {ex.Message}");
                    throw;
                }
                catch (Exception ex)
                {
                    LibraryLogger.Error(
                        $"SerialPort (LargeRaw) Write Error: Wrote {bytesWritten}/{totalBytes} bytes. Error: {ex.Message} : '{_port.PortName}'");
                    throw new IOException(
                        $"SerialPort (LargeRaw) Write Error after {bytesWritten} bytes: {ex.Message}", ex);
                }
            }

            if (bytesWritten != totalBytes)
            {
                LibraryLogger.Warning(
                    $"SerialPort (LargeRaw) Write Warning: Sent {bytesWritten}/{totalBytes} bytes.");
            }
        }

        if (_libUsbWriter != null) // LibUSB handles large data sends efficiently itself
        {
            var writeTimeout = _libUsbTimeoutMs > 0 ? _libUsbTimeoutMs : 5000;
            var ec = _libUsbWriter.Write(largeData, writeTimeout, out var libUsbBytesWritten);
            if (ec != Error.Success)
            {
                throw new IOException($"LibUsbDotNet WriteLargeRawData Error: {ec}");
            }

            if (libUsbBytesWritten != largeData.Length)
            {
                LibraryLogger.Warning(
                    $"LibUsbDotNet WriteLargeRawData Warning: Sent {libUsbBytesWritten}/{largeData.Length} bytes.");
            }
        }
    }

    public void SendData(byte[] data)
    {
        ObjectDisposedException.ThrowIf(_disposed, typeof(QualcommSerial));

        if (_port != null)
        {
            LibraryLogger.Trace($"Sending {data.Length} bytes via SerialPort.");
            _port.Write(data, 0, data.Length);
        }
        else if (_libUsbWriter != null)
        {
            var writeTimeout = _libUsbTimeoutMs > 0 ? _libUsbTimeoutMs : 5000;
            var ec = _libUsbWriter.Write(data, writeTimeout, out var bytesWritten);
            if (ec != Error.Success)
            {
                throw new IOException($"LibUsbDotNet Write Error: {ec}");
            }

            if (bytesWritten != data.Length)
            {
                LibraryLogger.Warning(
                    $"LibUsbDotNet Write Warning: Attempted to send {data.Length} bytes, but only {bytesWritten} were confirmed written by the call. Error code: {ec}");
            }
        }
        else
        {
            throw new InvalidOperationException(
                "No active communication channel (SerialPort or LibUsb) available to send data.");
        }
    }

    public void SendZeroLengthPacket()
    {
        ObjectDisposedException.ThrowIf(_disposed, typeof(QualcommSerial));
        if (_mode == CommunicationMode.LibUsbDotNet && _libUsbWriter != null)
        {
            LibraryLogger.Debug("Sending Zero-Length Packet (ZLP) via LibUsbDotNet.");
            var writeTimeout = _libUsbTimeoutMs > 0 ? _libUsbTimeoutMs : 1000;
            byte[] zlp = [];
            var ec = _libUsbWriter.Write(zlp, writeTimeout, out var bytesWritten);
            if (ec != Error.Success)
            {
                LibraryLogger.Warning($"LibUsbDotNet ZLP Write Error/Warning: {ec}. Bytes written: {bytesWritten}");
            }
            else
            {
                LibraryLogger.Trace("ZLP sent successfully via LibUsbDotNet.");
            }
        }
        else if (_port != null)
        {
            LibraryLogger.Trace("ZLP requested but using SerialPort backend; ZLP not applicable/sent.");
        }
    }

    public byte[] SendCommand(byte[] command, byte[]? responsePattern)
    {
        ObjectDisposedException.ThrowIf(_disposed, typeof(QualcommSerial));
        SendData(command);
        return GetResponse(responsePattern);
    }

    public byte[] GetResponse(byte[]? responsePattern, int length = 0x2000)
    {
        ObjectDisposedException.ThrowIf(_disposed, typeof(QualcommSerial));
        var responseBuffer = new byte[length > 0 ? length : 0x2000];
        length = 0;

        try
        {
            var bytesRead = 0;

            if (_port != null)
            {
                LibraryLogger.Trace($"{length}, {responseBuffer.Length}");
                bytesRead = _port.Read(responseBuffer, length, responseBuffer.Length - length);
                LibraryLogger.Trace($"{length}, {responseBuffer.Length} BytesRead: {bytesRead}");
            }

            if (_libUsbReader != null)
            {
                var readTimeout = _libUsbTimeoutMs > 0 ? _libUsbTimeoutMs : 1000;
                var ec = _libUsbReader.Read(responseBuffer, readTimeout, out bytesRead);

                LibraryLogger.Trace($"libUsb: {ec} - BytesRead: {bytesRead}");

                if (ec == Error.Success && bytesRead == 0)
                {
                    // Handle Zero Length Packets
                    ec = _libUsbReader.Read(responseBuffer, readTimeout, out bytesRead);
                    LibraryLogger.Trace($"Retry after ZLP: status: {ec} - BytesRead: {bytesRead}");
                }

                if (ec == Error.Timeout)
                {
                    throw new TimeoutException("LibUsbDotNet Read Timeout");
                }
            }

            if (bytesRead == 0)
            {
                LibraryLogger.Warning("Emergency mode of phone is ignoring us");
                throw new BadMessageException();
            }

            length += bytesRead;
            byte[] response;
            response = new byte[length];
            Buffer.BlockCopy(responseBuffer, 0, response, 0, length);

            if (responsePattern != null)
            {
                for (var i = 0; i < responsePattern.Length; i++)
                {
                    if (response[i] != responsePattern[i])
                    {
                        var logResponse = new byte[response.Length < 0x10 ? response.Length : 0x10];
                        LibraryLogger.Error("Qualcomm serial response: " +
                                            Converter.ConvertHexToString(logResponse, ""));
                        LibraryLogger.Error("Expected: " + Converter.ConvertHexToString(responsePattern, ""));
                        throw new BadMessageException();
                    }
                }
            }

            return response;
        }
        catch (TimeoutException)
        {
            throw new TimeoutException();
        }
        catch (Exception ex)
        {
            LibraryLogger.Error($"Error while reading response: {ex.Message}");
        }

        _port?.DiscardInBuffer();

        throw new BadConnectionException();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~QualcommSerial()
    {
        Dispose(false);
    }

    public void Close()
    {
        Dispose();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _port?.Dispose();
            if (_libUsbDevice != null)
            {
                try
                {
                    _ = _libUsbDevice.ReleaseInterface(0);
                    _libUsbDevice.Close();
                }
                catch (Exception ex) { LibraryLogger.Error($"Error disposing LibUsbDevice: {ex.Message}"); }
                finally { _libUsbDevice = null; }
            }
        }

        _libUsbDevice = null;
        _libUsbReader = null;
        _libUsbWriter = null;
        _disposed = true;
    }

    public void SetTimeOut(int v)
    {
        if (_libUsbDevice != null)
        {
            _libUsbTimeoutMs = v;
        }

        if (_port != null)
        {
            _port.ReadTimeout = v;
            _port.WriteTimeout = v;
        }
    }
}

public class BadMessageException : Exception
{
    public BadMessageException()
    {
    }

    public BadMessageException(string message) : base(message) { }
    public BadMessageException(string message, Exception innerException) : base(message, innerException) { }
}

public class BadConnectionException : Exception
{
    public BadConnectionException()
    {
    }

    public BadConnectionException(string message) : base(message) { }
    public BadConnectionException(string message, Exception innerException) : base(message, innerException) { }
}