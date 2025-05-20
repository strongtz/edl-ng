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

using System.IO.Ports;
using System.Runtime.InteropServices;
using LibUsbDotNet;
using LibUsbDotNet.Main;
using LibUsbDotNet.LibUsb;
using QCEDL.NET.Logging;

namespace Qualcomm.EmergencyDownload.Transport
{
    public enum CommunicationMode
    {
        None,
        SerialPort,
        LibUsbDotNet
    }

    public class QualcommSerial : IDisposable
    {
        private bool Disposed = false;
        private readonly SerialPort Port = null;

        public static UsbContext LibUsbContext { get; private set; } = null;
        private UsbDevice? _libUsbDevice = null;
        private UsbEndpointReader? _libUsbReader = null;
        private UsbEndpointWriter? _libUsbWriter = null;
        private CommunicationMode _mode = CommunicationMode.None;

        private int _libUsbTimeoutMs = 1000;

        public CommunicationMode ActiveCommunicationMode { get; private set; } = CommunicationMode.None;

        // Static constructor for LibUsb context
        static QualcommSerial()
        {
            try
            {
                LibUsbContext = new UsbContext();
                LibUsbContext.SetDebugLevel(LibUsbDotNet.LogLevel.Warning);
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
                if (deviceIdOrPath.StartsWith("/dev/tty"))
                {
                    Port = new SerialPort(deviceIdOrPath, 115200)
                    {
                        ReadTimeout = 1000,
                        WriteTimeout = 1000,
                    };
                    if (Port != null)
                    {
                        try
                        {
                            int desiredBufferSize = 1024 * 1024;
                            Port.WriteBufferSize = desiredBufferSize;
                            LibraryLogger.Debug($"SerialPort: Attempted to set WriteBufferSize to {desiredBufferSize}. Actual: {Port.WriteBufferSize}");
                        }
                        catch (Exception ex)
                        {
                            LibraryLogger.Warning($"SerialPort: Failed to set WriteBufferSize. Error: {ex.Message}");
                        }
                    }

                    Port.Open();
                    _mode = CommunicationMode.SerialPort;
                    ActiveCommunicationMode = _mode;
                    LibraryLogger.Debug($"Using System.IO.Ports for {deviceIdOrPath} on Linux. Timeout: {_libUsbTimeoutMs}ms");
                }
                else
                {
                    LibraryLogger.Debug($"Attempting LibUsbDotNet backend for {deviceIdOrPath}...");
                    try
                    {
                        (int vid, int pid) = ExtractVidPidFromDevicePath(deviceIdOrPath);
                        if (vid == 0 || pid == 0)
                        {
                            throw new Exception("Could not extract VID/PID from device path for LibUsbDotNet.");
                        }
                        LibraryLogger.Debug($"Searching LibUsb for VID=0x{vid:X4}, PID=0x{pid:X4}");
                        UsbDeviceFinder finder = new UsbDeviceFinder
                        {
                            Vid = vid,
                            Pid = pid
                        };

                        _libUsbDevice = (UsbDevice)LibUsbContext.Find(finder);
                        if (_libUsbDevice == null)
                        {
                            throw new Exception($"LibUsbDotNet: Device with VID=0x{vid:X4}, PID=0x{pid:X4} not found.");
                        }
                        _libUsbDevice.Open();

                        // seems to be unnecessary
                        // _libUsbDevice.SetConfiguration(1);

                        int interfaceNumberToClaim = 0;
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                        {
                            if (_libUsbDevice.SupportsDetachKernelDriver())
                            {
                                LibraryLogger.Debug("Platform supports kernel driver detachment.");
                                if (_libUsbDevice.IsKernelDriverActive(interfaceNumberToClaim))
                                {
                                    LibraryLogger.Info($"Kernel driver is active on interface {interfaceNumberToClaim}, attempting manual detach...");
                                    try { _libUsbDevice.DetachKernelDriver(interfaceNumberToClaim); LibraryLogger.Info("Manual detach successful."); }
                                    catch (UsbException ue) { LibraryLogger.Error($"Manual detach failed: {ue.Message}"); }
                                }
                            }
                            else
                            {
                                LibraryLogger.Info("Platform does not support kernel driver detachment or libusb cannot determine support. Proceeding...");
                            }
                        }

                        _libUsbDevice.ClaimInterface(0);

                        _libUsbReader = _libUsbDevice.OpenEndpointReader(ReadEndpointID.Ep01);
                        _libUsbWriter = _libUsbDevice.OpenEndpointWriter(WriteEndpointID.Ep01);
                        if (_libUsbReader == null || _libUsbWriter == null)
                        {
                            throw new Exception("LibUsbDotNet: Could not open required bulk IN/OUT endpoints.");
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
                string[] DevicePathElements = deviceIdOrPath.Split(['#']);
                if (string.Equals(DevicePathElements[3], "{86E0D1E0-8089-11D0-9CE4-08003E301F73}", StringComparison.CurrentCultureIgnoreCase))
                {
                    string PortName = (string)Microsoft.Win32.Registry.GetValue($@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Enum\USB\{DevicePathElements[1]}\{DevicePathElements[2]}\Device Parameters", "PortName", null);
                    if (PortName != null)
                    {
                        try
                        {
                            Port = new SerialPort(PortName, 115200)
                            {
                                ReadTimeout = 1000,
                                WriteTimeout = 1000
                            };
                            if (Port != null)
                            {
                                try
                                {
                                    int desiredBufferSize = 1024 * 1024;
                                    Port.ReadBufferSize = desiredBufferSize;
                                    Port.WriteBufferSize = desiredBufferSize;
                                    LibraryLogger.Debug($"SerialPort: Attempted ReadBufferSize={desiredBufferSize}. Actual: {Port.ReadBufferSize}");
                                    LibraryLogger.Debug($"SerialPort: Attempted to set WriteBufferSize to {desiredBufferSize}. Actual: {Port.WriteBufferSize}");
                                }
                                catch (Exception ex)
                                {
                                    LibraryLogger.Warning($"SerialPort: Failed to set WriteBufferSize. Error: {ex.Message}");
                                }
                            }
                            Port.Open();
                            _mode = CommunicationMode.SerialPort;
                            ActiveCommunicationMode = _mode;
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            LibraryLogger.Error($"Failed to open SerialPort: {ex.Message}");
                            LibraryLogger.Error($"Please check if the port is already in use by some Qualcomm software");
                            LibraryLogger.Error($"Try stopping the 'Qualcomm Unified Tools Service' if you have closed every other suspicious program");
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
                        (int vid, int pid) = ExtractVidPidFromDevicePath(deviceIdOrPath);
                        if (vid == 0 || pid == 0)
                        {
                            throw new Exception("Could not extract VID/PID from device path for LibUsbDotNet.");
                        }
                        LibraryLogger.Debug($"Searching LibUsb for VID=0x{vid:X4}, PID=0x{pid:X4}");
                        UsbDeviceFinder finder = new UsbDeviceFinder
                        {
                            Vid = vid,
                            Pid = pid
                            // You can also set SerialNumber here if needed:
                            // SerialNumber = "YourSerialNumber"
                        };
                        _libUsbDevice = (UsbDevice)LibUsbContext.Find(finder);
                        if (_libUsbDevice == null)
                        {
                            throw new Exception($"LibUsbDotNet: Device with VID=0x{vid:X4}, PID=0x{pid:X4} not found.");
                        }
                        _libUsbDevice.Open();

                        _libUsbDevice.SetConfiguration(1);
                        _libUsbDevice.ClaimInterface(0);

                        _libUsbReader = _libUsbDevice.OpenEndpointReader(ReadEndpointID.Ep01);
                        _libUsbWriter = _libUsbDevice.OpenEndpointWriter(WriteEndpointID.Ep01);
                        if (_libUsbReader == null || _libUsbWriter == null)
                        {
                            throw new Exception("LibUsbDotNet: Could not open required bulk IN/OUT endpoints.");
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

        private (int vid, int pid) ExtractVidPidFromDevicePath(string devicePath)
        {
            int vid = 0;
            int pid = 0;
            try
            {
                var matchVid = System.Text.RegularExpressions.Regex.Match(devicePath, @"VID_([0-9A-Fa-f]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var matchPid = System.Text.RegularExpressions.Regex.Match(devicePath, @"PID_([0-9A-Fa-f]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (matchVid.Success) int.TryParse(matchVid.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out vid);
                if (matchPid.Success) int.TryParse(matchPid.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out pid);
            }
            catch { /* Ignore parsing errors */ }
            return (vid, pid);
        }

        // Method for sending large raw data (e.g., for Firehose program) with internal chunking for SerialPort
        public void SendLargeRawData(byte[] largeData)
        {
            if (Port != null)
            {
                int bytesWritten = 0;
                int totalBytes = largeData.Length;
                int chunkSize = Port.WriteBufferSize > 0 ? Port.WriteBufferSize : 2048; // Default to 2KB if not set
                if (chunkSize <= 0) chunkSize = 4096;
                LibraryLogger.Trace($"SerialPort (LargeRaw): Sending {totalBytes} bytes in chunks of {chunkSize}. Timeout: {Port.WriteTimeout}ms");
                while (bytesWritten < totalBytes)
                {
                    int bytesToWriteThisChunk = Math.Min(chunkSize, totalBytes - bytesWritten);
                    try
                    {
                        Port.Write(largeData, bytesWritten, bytesToWriteThisChunk);
                        bytesWritten += bytesToWriteThisChunk;
                        LibraryLogger.Trace($"SerialPort (LargeRaw): Wrote chunk of {bytesToWriteThisChunk} bytes. Total written: {bytesWritten}/{totalBytes}");
                    }
                    catch (TimeoutException ex)
                    {
                        LibraryLogger.Error($"SerialPort (LargeRaw) Write Timeout: Wrote {bytesWritten}/{totalBytes} bytes. {ex.Message}");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        LibraryLogger.Error($"SerialPort (LargeRaw) Write Error: Wrote {bytesWritten}/{totalBytes} bytes. Error: {ex.Message} : '{Port.PortName}'");
                        throw new IOException($"SerialPort (LargeRaw) Write Error after {bytesWritten} bytes: {ex.Message}", ex);
                    }
                }
                if (bytesWritten != totalBytes)
                {
                    LibraryLogger.Warning($"SerialPort (LargeRaw) Write Warning: Sent {bytesWritten}/{totalBytes} bytes.");
                }
            }
            if (_libUsbWriter != null) // LibUSB handles large data sends efficiently itself
            {
                int writeTimeout = _libUsbTimeoutMs > 0 ? _libUsbTimeoutMs : 5000;
                Error ec = _libUsbWriter.Write(largeData, writeTimeout, out int libUsbBytesWritten);
                if (ec != Error.Success)
                {
                    throw new IOException($"LibUsbDotNet WriteLargeRawData Error: {ec}");
                }
                if (libUsbBytesWritten != largeData.Length)
                {
                    LibraryLogger.Warning($"LibUsbDotNet WriteLargeRawData Warning: Sent {libUsbBytesWritten}/{largeData.Length} bytes.");
                }
            }
        }

        public void SendData(byte[] Data)
        {
            if (Disposed) throw new ObjectDisposedException(nameof(QualcommSerial));

            if (Port != null)
            {
                LibraryLogger.Trace($"Sending {Data.Length} bytes via SerialPort.");
                Port.Write(Data, 0, Data.Length);
            }
            else if (_libUsbWriter != null)
            {
                int writeTimeout = _libUsbTimeoutMs > 0 ? _libUsbTimeoutMs : 5000;
                Error ec = _libUsbWriter.Write(Data, writeTimeout, out int bytesWritten);
                if (ec != Error.Success)
                {
                    throw new IOException($"LibUsbDotNet Write Error: {ec}");
                }
                if (bytesWritten != Data.Length)
                {
                    LibraryLogger.Warning($"LibUsbDotNet Write Warning: Attempted to send {Data.Length} bytes, but only {bytesWritten} were confirmed written by the call. Error code: {ec}");
                }
            }
            else
            {
                throw new InvalidOperationException("No active communication channel (SerialPort or LibUsb) available to send data.");
            }
        }

        public void SendZeroLengthPacket()
        {
            if (Disposed) throw new ObjectDisposedException(nameof(QualcommSerial));
            if (_mode == CommunicationMode.LibUsbDotNet && _libUsbWriter != null)
            {
                LibraryLogger.Debug("Sending Zero-Length Packet (ZLP) via LibUsbDotNet.");
                int writeTimeout = _libUsbTimeoutMs > 0 ? _libUsbTimeoutMs : 1000;
                byte[] zlp = [];
                Error ec = _libUsbWriter.Write(zlp, writeTimeout, out int bytesWritten);
                if (ec != Error.Success)
                {
                    LibraryLogger.Warning($"LibUsbDotNet ZLP Write Error/Warning: {ec}. Bytes written: {bytesWritten}");
                }
                else
                {
                    LibraryLogger.Trace("ZLP sent successfully via LibUsbDotNet.");
                }
            }
            else if (Port != null)
            {
                LibraryLogger.Trace("ZLP requested but using SerialPort backend; ZLP not applicable/sent.");
            }
        }

        public byte[] SendCommand(byte[] Command, byte[] ResponsePattern)
        {
            if (Disposed) throw new ObjectDisposedException(nameof(QualcommSerial));
            SendData(Command);
            return GetResponse(ResponsePattern);
        }

        public byte[] GetResponse(byte[] ResponsePattern, int Length = 0x2000)
        {
            if (Disposed) throw new ObjectDisposedException(nameof(QualcommSerial));
            byte[] ResponseBuffer = new byte[Length > 0 ? Length : 0x2000];
            Length = 0;

            try
            {
                int BytesRead = 0;

                if (Port != null)
                {
                    LibraryLogger.Trace($"{Length}, {ResponseBuffer.Length}");
                    BytesRead = Port.Read(ResponseBuffer, Length, ResponseBuffer.Length - Length);
                    LibraryLogger.Trace($"{Length}, {ResponseBuffer.Length} BytesRead: {BytesRead}");
                }

                if (_libUsbReader != null)
                {
                    int readTimeout = _libUsbTimeoutMs > 0 ? _libUsbTimeoutMs : 1000;
                    Error ec = _libUsbReader.Read(ResponseBuffer, readTimeout, out BytesRead);

                    LibraryLogger.Trace($"libUsb: {ec} - BytesRead: {BytesRead}");

                    if (ec == Error.Success && BytesRead == 0)
                    {
                        // Handle Zero Length Packets
                        ec = _libUsbReader.Read(ResponseBuffer, readTimeout, out BytesRead);
                        LibraryLogger.Trace($"Retry after ZLP: status: {ec} - BytesRead: {BytesRead}");
                    }

                    if (ec == Error.Timeout) throw new TimeoutException("LibUsbDotNet Read Timeout");
                }

                if (BytesRead == 0)
                {
                    LibraryLogger.Warning("Emergency mode of phone is ignoring us");
                    throw new BadMessageException();
                }

                Length += BytesRead;
                byte[] Response;
                Response = new byte[Length];
                Buffer.BlockCopy(ResponseBuffer, 0, Response, 0, Length);

                if (ResponsePattern != null)
                {
                    for (int i = 0; i < ResponsePattern.Length; i++)
                    {
                        if (Response[i] != ResponsePattern[i])
                        {
                            byte[] LogResponse = new byte[Response.Length < 0x10 ? Response.Length : 0x10];
                            LibraryLogger.Error("Qualcomm serial response: " + Converter.ConvertHexToString(LogResponse, ""));
                            LibraryLogger.Error("Expected: " + Converter.ConvertHexToString(ResponsePattern, ""));
                            throw new BadMessageException();
                        }
                    }
                }

                return Response;
            }
            catch (TimeoutException)
            {
                throw new TimeoutException();
            }
            catch (Exception ex)
            {
                LibraryLogger.Error($"Error while reading response: {ex.Message}");
            }

            Port?.DiscardInBuffer();

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
            if (Disposed) return;
            if (disposing)
            {
                Port?.Dispose();
                if (_libUsbDevice != null)
                {
                    try
                    {
                        _libUsbDevice.ReleaseInterface(0);
                        _libUsbDevice.Close();
                    }
                    catch (Exception ex) { LibraryLogger.Error($"Error disposing LibUsbDevice: {ex.Message}"); }
                    finally { _libUsbDevice = null; }
                }
            }
            _libUsbDevice = null;
            _libUsbReader = null;
            _libUsbWriter = null;
            Disposed = true;
        }

        public void SetTimeOut(int v)
        {
            if (_libUsbDevice != null)
            {
                _libUsbTimeoutMs = v;
            }

            if (Port != null)
            {
                Port.ReadTimeout = v;
                Port.WriteTimeout = v;
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
}
