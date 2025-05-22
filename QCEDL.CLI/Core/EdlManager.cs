using QCEDL.CLI.Helpers;
using QCEDL.NET.USB;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;
using Qualcomm.EmergencyDownload.Layers.PBL.Sahara;
using Qualcomm.EmergencyDownload.Transport;
using System.Text;
using System.Runtime.InteropServices;
using LibUsbDotNet.Main;
using QCEDL.NET.Todo;

namespace QCEDL.CLI.Core;

internal sealed class EdlManager : IDisposable
{
    private readonly GlobalOptionsBinder _globalOptions;
    private string? _devicePath;
    private Guid? _deviceGuid;
    private QualcommSerial? _serialPort;
    private QualcommSahara? _saharaClient;
    private QualcommFirehose? _firehoseClient;
    private bool _disposed;

    // GUIDs for device detection on Windows
    private static readonly Guid COMPortGuid = new("{86E0D1E0-8089-11D0-9CE4-08003E301F73}");
    private static readonly Guid WinUSBGuid = new("{a5dcbf10-6530-11d2-901f-00c04fb951ed}");

    private DeviceMode _currentMode = DeviceMode.Unknown;
    private byte[]? _initialSaharaHelloPacket;

    public DeviceMode CurrentMode => _currentMode;

    public QualcommFirehose Firehose => _firehoseClient ?? throw new InvalidOperationException("Not connected in Firehose mode.");
    public bool IsFirehoseMode => _firehoseClient != null;

    public EdlManager(GlobalOptionsBinder globalOptions)
    {
        _globalOptions = globalOptions;
    }

    /// <summary>
    /// Attempts to detect the current operating mode of the connected EDL device.
    /// Connects temporarily if not already connected.
    /// </summary>
    /// <returns>The detected DeviceMode.</returns>
    public async Task<DeviceMode> DetectCurrentModeAsync(bool forceReconnect = false)
    {
        if (_currentMode != DeviceMode.Unknown && _serialPort != null && !forceReconnect)
        {
            Logging.Log($"Using cached device mode: {_currentMode}", LogLevel.Debug);
            return _currentMode;
        }

        if (string.IsNullOrEmpty(_devicePath))
        {
            if (!FindDevice())
            {
                Logging.Log("Cannot detect mode: No device found.", LogLevel.Error);
                _currentMode = DeviceMode.Error;
                return DeviceMode.Error;
            }
        }

        if (forceReconnect)
        {
            _serialPort?.Dispose();
            _serialPort = null;
            _saharaClient = null;
            _firehoseClient = null;
            _currentMode = DeviceMode.Unknown;
            _initialSaharaHelloPacket = null;
        }

        Logging.Log("Probing device mode...", LogLevel.Debug);
        QualcommSerial? probeSerial = null;
        var detectedMode = DeviceMode.Unknown;
        byte[]? initialReadBuffer = null;

        try
        {
            probeSerial = new QualcommSerial(_devicePath!);
            probeSerial.SetTimeOut(500); // Short timeout for initial read attempt
            // --- Probe 1: Passive Read ---
            Logging.Log("Attempting passive read...", LogLevel.Debug);
            try
            {
                // Try reading a small amount.
                Logging.Log("Reading initial data from device...", LogLevel.Debug);
                initialReadBuffer = probeSerial.GetResponse(null, 48); // Read up to 48 bytes raw
                Logging.Log("Initial read completed.", LogLevel.Debug);
            }
            catch (TimeoutException)
            {
                Logging.Log("Passive read timed out (no initial data from device).", LogLevel.Debug);
                initialReadBuffer = null;
            }
            catch (BadMessageException)
            {
                Logging.Log("Passive read received potentially incomplete/bad message.", LogLevel.Debug);
                initialReadBuffer = null;
            }
            catch (Exception ex)
            {
                Logging.Log($"Error during passive read: {ex.Message}", LogLevel.Debug);
                initialReadBuffer = null;
            }

            if (initialReadBuffer != null && initialReadBuffer.Length > 0)
            {
                Logging.Log($"Passive read got {initialReadBuffer.Length} bytes: {Convert.ToHexString(initialReadBuffer)}", LogLevel.Debug);
                // Check for Sahara HELLO (starts with 0x01 0x00 0x00 0x00)
                if (initialReadBuffer.Length >= 4 && initialReadBuffer[0] == 0x01 && initialReadBuffer[1] == 0x00 && initialReadBuffer[2] == 0x00 && initialReadBuffer[3] == 0x00)
                {
                    Logging.Log("Passive read matches Sahara HELLO pattern. Detected Mode: Sahara", LogLevel.Debug);
                    detectedMode = DeviceMode.Sahara;
                    _initialSaharaHelloPacket = initialReadBuffer;

                    _serialPort = probeSerial; // Keep the probeSerial as the main _serialPort
                    probeSerial = null; // Nullify probeSerial so it's not disposed in finally if kept
                    _saharaClient = new QualcommSahara(_serialPort);
                    _currentMode = DeviceMode.Sahara;

                }
                // Check for Firehose XML start
                else if (Encoding.UTF8.GetString(initialReadBuffer).Contains("<?xml"))
                {
                    Logging.Log("Passive read contains XML declaration. Detected Mode: Firehose", LogLevel.Debug);
                    detectedMode = DeviceMode.Firehose;
                }
                else
                {
                    Logging.Log("Passive read received unexpected data.", LogLevel.Debug);
                }
            }

            // --- Probe 2: Active Firehose NOP (If Passive Read Failed/Inconclusive) ---
            if (detectedMode == DeviceMode.Unknown)
            {
                var serialForFirehoseProbe = _serialPort ?? probeSerial;
                if (serialForFirehoseProbe == null)
                {
                    Logging.Log("No serial port available for Firehose NOP probe.", LogLevel.Error);
                }
                else
                {
                    Logging.Log("Passive read inconclusive. Attempting active Firehose NOP probe...", LogLevel.Debug);
                    try
                    {
                        serialForFirehoseProbe.SetTimeOut(1500);
                        var firehoseProbe = new QualcommFirehose(serialForFirehoseProbe);
                        var nopCommand = QualcommFirehoseXml.BuildCommandPacket([new Data() { Nop = new Nop() }]);
                        firehoseProbe.Serial.SendData(Encoding.UTF8.GetBytes(nopCommand));
                        var datas = await Task.Run(() => firehoseProbe.GetFirehoseResponseDataPayloads());
                        if (datas.Length > 0)
                        {
                            Logging.Log("Firehose NOP probe successful. Detected Mode: Firehose", LogLevel.Debug);
                            detectedMode = DeviceMode.Firehose;

                            Logging.Log("Flushing serial output...", LogLevel.Debug);
                            // Logging.Log("Reading initial data from device...", LogLevel.Debug);
                            // FlushForResponse();
                            var GotResponse = false;
                            try
                            {
                                while (!GotResponse)
                                {
                                    var flushingDatas = firehoseProbe.GetFirehoseResponseDataPayloads();

                                    foreach (var data in flushingDatas)
                                    {
                                        if (data.Log != null)
                                        {
                                            Logging.Log("DEVPRG LOG: " + data.Log.Value, LogLevel.Debug);
                                        }
                                        else if (data.Response != null)
                                        {
                                            GotResponse = true;
                                        }
                                    }
                                }
                            }
                            catch (BadConnectionException) { }
                        }
                        else
                        {
                            Logging.Log("Firehose NOP probe: No XML response received.", LogLevel.Debug);
                        }
                    }
                    catch (TimeoutException) { Logging.Log("Firehose NOP probe timed out.", LogLevel.Debug); }
                    catch (BadConnectionException) { Logging.Log("Firehose NOP probe failed (bad connection).", LogLevel.Debug); }
                    catch (BadMessageException) { Logging.Log("Firehose NOP probe failed (bad message).", LogLevel.Debug); }
                    catch (Exception ex) { Logging.Log($"Firehose NOP probe failed unexpectedly: {ex.Message}", LogLevel.Debug); }
                }
            }

            // --- Probe 3: Active Sahara Handshake (If Still Unknown) ---
            if (detectedMode == DeviceMode.Unknown)
            {
                var serialForSaharaProbe = _serialPort ?? probeSerial;
                if (serialForSaharaProbe == null)
                {
                    Logging.Log("No serial port available for Sahara handshake probe.", LogLevel.Error);
                }
                else
                {
                    Logging.Log("Probes inconclusive. Attempting *full* Sahara handshake as last resort...", LogLevel.Info);
                    try
                    {
                        serialForSaharaProbe.SetTimeOut(2000);
                        var saharaProbeClient = new QualcommSahara(serialForSaharaProbe);
                        // Pass null to CommandHandshake as we don't have a pre-read packet here
                        if (await Task.Run(() => saharaProbeClient.CommandHandshake(null)))
                        {
                            Logging.Log("Full Sahara handshake probe successful. Detected Mode: Sahara", LogLevel.Info);
                            detectedMode = DeviceMode.Sahara;
                            // If successful, this becomes the main connection
                            _serialPort = serialForSaharaProbe;
                            probeSerial = null; // Don't dispose it
                            _saharaClient = saharaProbeClient;
                            _currentMode = DeviceMode.Sahara;
                            try { _saharaClient.ResetSahara(); } catch { /* Ignore reset errors */ }
                        }
                        else
                        {
                            Logging.Log("Full Sahara handshake probe failed.", LogLevel.Warning);
                        }
                    }
                    catch (TimeoutException) { Logging.Log("Full Sahara handshake probe timed out.", LogLevel.Debug); }
                    catch (BadConnectionException) { Logging.Log("Full Sahara handshake probe failed (bad connection).", LogLevel.Debug); }
                    catch (BadMessageException) { Logging.Log("Full Sahara handshake probe failed (bad message - likely not Sahara).", LogLevel.Debug); }
                    catch (Exception ex) { Logging.Log($"Full Sahara handshake probe failed unexpectedly: {ex.Message}", LogLevel.Debug); }
                }
            }
            // --- Probe 4: Add streaming probe if needed ---
        }
        catch (Exception ex)
        {
            Logging.Log($"Error during mode detection: {ex.Message}", LogLevel.Error);
            detectedMode = DeviceMode.Error;
        }
        finally
        {
            // Dispose probeSerial ONLY if it wasn't kept as the main _serialPort
            if (probeSerial != null && probeSerial != _serialPort)
            {
                probeSerial.Dispose();
            }
        }

        if (_currentMode == DeviceMode.Unknown)
        {
            _currentMode = detectedMode;
        }
        Logging.Log($"Detected device mode: {_currentMode}", LogLevel.Info);
        return _currentMode;
    }

    /// <summary>
    /// Finds a compatible EDL device.
    /// </summary>
    /// <returns>True if a device is found, false otherwise.</returns>
    /// <exception cref="Exception">Throws if multiple devices are found without specific selection criteria.</exception>
    private bool FindDevice()
    {
        Logging.Log("Searching for Qualcomm EDL device...", LogLevel.Trace);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (FindDeviceLinuxLibUsb())
            {
                Logging.Log("Found device using LibUsbDotNet on Linux / MacOS.", LogLevel.Info);
                return true;
            }
            // Fallback to Serial Port on Linux
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Logging.Log("LibUsbDotNet device detection failed or no device found. Falling back to Serial Port (ttyUSB/ttyACM) detection on Linux...", LogLevel.Warning);
                Logging.Log("Serial Port is known to be broken on Linux. Please double check why LibUsb is not working if device detection succeeds with Serial Port.", LogLevel.Error);
                return FindDeviceLinuxSerial();
            }
            return false;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return FindDeviceWindows();
        }
        else
        {
            Logging.Log($"Unsupported OS: {RuntimeInformation.OSDescription}. Device discovery skipped.", LogLevel.Error);
            return false;
        }
    }

    private bool FindDeviceLinuxLibUsb()
    {
        Logging.Log("Trying to find device using LibUsbDotNet on Linux / MacOS...", LogLevel.Debug);
        if (QualcommSerial.LibUsbContext == null)
        {
            Logging.Log("LibUsbDotNet context not initialized. Cannot use LibUsb backend.", LogLevel.Warning);
            return false;
        }
        var vidToFind = _globalOptions.Vid ?? 0x05C6;
        var pidToFind = _globalOptions.Pid ?? 0x9008;
        // string serialToFind = _globalOptions.SerialNumber; // If you add a serial number option
        try
        {
            var finder = new UsbDeviceFinder{Vid = vidToFind, Pid = pidToFind};
            // if (!string.IsNullOrEmpty(serialToFind)) finder.SerialNumber = serialToFind;

            var usbDevice = QualcommSerial.LibUsbContext.Find(finder);
            if (usbDevice != null)
            {
                // _libUsbSerialNumber = serialToFind; // If used
                _devicePath = $"usb:vid_{vidToFind:X4},pid_{pidToFind:X4}";
                _deviceGuid = WinUSBGuid; // Use WinUSBGuid to signify LibUsbDotNet backend to QualcommSerial
                Logging.Log($"LibUsbDotNet found device: VID={vidToFind:X4}, PID={pidToFind:X4}. Path set to: {_devicePath}", LogLevel.Debug);
                return true;
            }
            else
            {
                Logging.Log($"LibUsbDotNet: No device found with VID={vidToFind:X4}, PID={pidToFind:X4}.", LogLevel.Debug);
                return false;
            }
        }
        catch (Exception ex)
        {
            Logging.Log($"Error during LibUsbDotNet device discovery on Linux: {ex.Message}", LogLevel.Error);
            Logging.Log(ex.ToString(), LogLevel.Debug);
            return false;
        }
    }

    private bool FindDeviceLinuxSerial()
    {
        Logging.Log("Searching for Qualcomm EDL device on Linux (/dev/ttyUSB* or /dev/ttyACM*)...", LogLevel.Info);
        var targetVid = (_globalOptions.Vid ?? 0x05C6).ToString("x4");
        var targetPid = (_globalOptions.Pid ?? 0x9008).ToString("x4");
        Logging.Log($"Target VID: {targetVid}, PID: {targetPid}", LogLevel.Debug);
        var potentialTtyPaths = new List<string>();
        try
        {
            var sysTtyPath = "/sys/class/tty";
            if (!Directory.Exists(sysTtyPath))
            {
                Logging.Log($"Sysfs tty path not found: {sysTtyPath}", LogLevel.Error);
                return false;
            }
            foreach (var dirName in Directory.GetDirectories(sysTtyPath))
            {
                var ttyName = Path.GetFileName(dirName);
                if (ttyName.StartsWith("ttyUSB") || ttyName.StartsWith("ttyACM"))
                {
                    Logging.Log($"TTY dirName: {dirName}", LogLevel.Trace);
                    var realDevicePath = new FileInfo(dirName).ResolveLinkTarget(true)?.FullName;
                    if (realDevicePath == null) continue;
                    Logging.Log($"Found TTY: {ttyName}, Real Device Path: {realDevicePath}", LogLevel.Trace);

                    var ttyDir = Directory.GetParent(realDevicePath);
                    if (ttyDir == null) continue;
                    var usbInterfaceDir = ttyDir.Parent;
                    if (usbInterfaceDir == null) continue;
                    var usbDeviceDir = usbInterfaceDir.Parent;
                    if (usbDeviceDir == null) continue;
                    if (ttyName.StartsWith("ttyUSB"))
                    {
                        // Another level up
                        usbDeviceDir = usbDeviceDir.Parent;
                        if (usbDeviceDir == null) continue;
                    }
                    var vidPath = Path.Combine(usbDeviceDir.FullName, "idVendor");
                    var pidPath = Path.Combine(usbDeviceDir.FullName, "idProduct");
                    if (File.Exists(vidPath) && File.Exists(pidPath))
                    {
                        var vid = File.ReadAllText(vidPath).Trim();

                        var pid = File.ReadAllText(pidPath).Trim();
                        Logging.Log($"Found TTY: {ttyName}, VID: {vid}, PID: {pid}", LogLevel.Trace);
                        if (vid.Equals(targetVid, StringComparison.OrdinalIgnoreCase) &&
                            pid.Equals(targetPid, StringComparison.OrdinalIgnoreCase))
                        {
                            var devPath = Path.Combine("/dev", ttyName);
                            if (File.Exists(devPath)) // Check if /dev/ttyUSBx actually exists
                            {
                                potentialTtyPaths.Add(devPath);
                                Logging.Log($"Match: {devPath} for VID/PID {targetVid}/{targetPid}", LogLevel.Debug);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logging.Log($"Error during Linux device discovery: {ex.Message}", LogLevel.Error);
            Logging.Log(ex.ToString(), LogLevel.Debug);
            return false;
        }
        if (potentialTtyPaths.Count == 0)
        {
            Logging.Log("No matching Qualcomm EDL serial devices found.", LogLevel.Error);
            return false;
        }
        if (potentialTtyPaths.Count > 1)
        {
            Logging.Log($"Multiple ({potentialTtyPaths.Count}) matching serial devices found. Using the first one: {potentialTtyPaths[0]}", LogLevel.Warning);
            foreach (var p in potentialTtyPaths)
            {
                Logging.Log($"  - Found: {p}", LogLevel.Warning);
            }
        }
        _devicePath = potentialTtyPaths[0];
        _deviceGuid = COMPortGuid; // For some relevant logic
        Logging.Log($"Selected device: {_devicePath}", LogLevel.Info);
        Logging.Log("  Mode detected: Serial Port (ttyUSB/ttyACM)", LogLevel.Info);
        return true;
    }

    private bool FindDeviceWindows()
    {
        Logging.Log("Searching for Qualcomm EDL device on Windows (Qualcomm Serial Driver or WinUSB)...", LogLevel.Info);

        // Store DevInst and the interface GUID it was found with
        var potentialDevices = new List<(string DevicePath, string BusName, Guid InterfaceGuid, int DevInst)>();
        // Search by COMPortGuid
        foreach (var deviceInfo in USBExtensions.GetDeviceInfos(COMPortGuid))
        {
            Logging.Log($"Found device via COMPortGuid: {deviceInfo.PathName} on bus {deviceInfo.BusName} (DevInst: {deviceInfo.DevInst})", LogLevel.Debug);
            if (deviceInfo.PathName is not null && IsQualcommEdlDevice(deviceInfo.PathName, deviceInfo.BusName))
            {
                potentialDevices.Add((deviceInfo.PathName, deviceInfo.BusName, COMPortGuid, deviceInfo.DevInst));
            }
        }
        // Search by WinUSBGuid
        foreach (var deviceInfo in USBExtensions.GetDeviceInfos(WinUSBGuid))
        {
            Logging.Log($"Found device via WinUSBGuid: {deviceInfo.PathName} on bus {deviceInfo.BusName} (DevInst: {deviceInfo.DevInst})", LogLevel.Debug);
            if (deviceInfo.PathName is not null && IsQualcommEdlDevice(deviceInfo.PathName, deviceInfo.BusName))
            {
                potentialDevices.Add((deviceInfo.PathName, deviceInfo.BusName, WinUSBGuid, deviceInfo.DevInst));
            }
        }
        // De-duplicate based on DevInst
        var uniqueEdlDevices = potentialDevices
            .GroupBy(d => d.DevInst) // Group by physical device instance
            .Select(g =>
            {
                // For each physical device, if it was found via COMPortGuid, prefer that.
                // Otherwise, take the first one found (which would be WinUSBGuid if COMPort wasn't a match,
                // or if it was only found via WinUSB).
                var preferredDevice = g.FirstOrDefault(item => item.InterfaceGuid == COMPortGuid);
                if (preferredDevice.DevicePath != null)
                {
                    Logging.Log($"Selecting device (DevInst: {preferredDevice.DevInst}) via COMPortGuid: {preferredDevice.DevicePath}", LogLevel.Debug);
                    return preferredDevice;
                }
                var fallbackDevice = g.First();
                Logging.Log($"Selecting device (DevInst: {fallbackDevice.DevInst}) via {(fallbackDevice.InterfaceGuid == WinUSBGuid ? "WinUSBGuid" : "OtherGuid")}: {fallbackDevice.DevicePath}", LogLevel.Debug);
                return fallbackDevice;
            })
            .ToList();
        if (uniqueEdlDevices.Count == 0)
        {
            Logging.Log("No Qualcomm EDL devices found.", LogLevel.Error);
            return false;
        }
        else if (uniqueEdlDevices.Count > 1)
        {
            Logging.Log($"Multiple ({uniqueEdlDevices.Count}) unique EDL devices found. Please specify which device to use.", LogLevel.Error);
            foreach (var dev in uniqueEdlDevices)
            {
                Logging.Log($"  - Path: {dev.DevicePath}, Bus: {dev.BusName}, Interface: {(dev.InterfaceGuid == COMPortGuid ? "COM Port" : "WinUSB")}", LogLevel.Error);
            }
            // For simplicity, pick the first one if multiple are found, or require user to specify.
            // return false; // Or handle selection
            Logging.Log($"Picking the first device: {uniqueEdlDevices[0].DevicePath}", LogLevel.Warning);
        }

        var selectedDevice = uniqueEdlDevices.First();
        Logging.Log($"Qualcomm EDL device selected: {selectedDevice.DevicePath} on bus {selectedDevice.BusName} (Interface: {(selectedDevice.InterfaceGuid == COMPortGuid ? "COM Port" : "WinUSB")}, DevInst: {selectedDevice.DevInst})", LogLevel.Debug);

        _devicePath = selectedDevice.DevicePath;
        _deviceGuid = selectedDevice.InterfaceGuid;

        Logging.Log($"Found device: {_devicePath}", LogLevel.Info);
        Logging.Log($"  Interface: {(_deviceGuid == COMPortGuid ? "Serial Port" : "libusb via WinUSB")}", LogLevel.Info);
        Logging.Log($"  Bus Name: {selectedDevice.BusName}", LogLevel.Debug);

        if (selectedDevice.BusName?.StartsWith("QUSB_BULK", StringComparison.OrdinalIgnoreCase) == true ||
            selectedDevice.BusName == "QHSUSB_DLOAD" ||
            selectedDevice.BusName == "QHSUSB__BULK")
        {
            Logging.Log("  Mode detected: Sahara/Firehose (9008)", LogLevel.Info);
        }
        else if (selectedDevice.BusName == "QHSUSB_ARMPRG")
        {
            Logging.Log("  Mode detected: Emergency Flash (9006/other)", LogLevel.Warning);
            // Not yet implemented
        }
        else
        {
            Logging.Log("  Mode detection based on BusName uncertain.", LogLevel.Warning);
        }

        return true;
    }

    private bool IsQualcommEdlDevice(string devicePath, string busName)
    {
        var isQualcomm = devicePath.Contains("VID_05C6&", StringComparison.OrdinalIgnoreCase);
        if (_globalOptions.Vid.HasValue && isQualcomm)
        {
            isQualcomm = devicePath.Contains($"VID_{_globalOptions.Vid.Value:X4}&", StringComparison.OrdinalIgnoreCase);
        }

        var isEdl = devicePath.Contains("&PID_9008", StringComparison.OrdinalIgnoreCase);
        if (_globalOptions.Pid.HasValue && isEdl)
        {
            isEdl = devicePath.Contains($"&PID_{_globalOptions.Pid.Value:X4}", StringComparison.OrdinalIgnoreCase);
        }

        return isQualcomm && isEdl;
    }

    /// <summary>
    /// Ensures the device is connected and in Firehose mode, uploading the loader if necessary.
    /// </summary>
    public async Task EnsureFirehoseModeAsync()
    {
        if (_currentMode == DeviceMode.Firehose && _firehoseClient != null)
        {
            Logging.Log("Already in Firehose mode.", LogLevel.Debug);
            return;
        }

        var mode = await DetectCurrentModeAsync(forceReconnect: true);
        switch (mode)
        {
            case DeviceMode.Firehose:
                Logging.Log("Device is in Firehose mode. Establishing connection...", LogLevel.Debug);
                _serialPort?.Dispose();
                _serialPort = new QualcommSerial(_devicePath!);
                _firehoseClient = new QualcommFirehose(_serialPort);
                break;
            case DeviceMode.Sahara:
                Logging.Log("Device is in Sahara mode. Uploading loader...", LogLevel.Info);
                await UploadLoaderViaSaharaAsync();
                Logging.Log("Waiting for device to re-enumerate in Firehose mode...", LogLevel.Debug);
                await Task.Delay(500);

                // Clear old path/state and find the device again
                _devicePath = null;
                _serialPort = null;
                _firehoseClient = null;
                _saharaClient = null;
                _currentMode = DeviceMode.Unknown;
                if (!FindDevice()) // Find the potentially new device path
                {
                    throw new TodoException("Device did not re-enumerate in Firehose mode after loader upload, or could not be found.");
                }
                // Now establish the Firehose connection
                Logging.Log("Connecting to re-enumerated device in Firehose mode...", LogLevel.Debug);
                _serialPort = new QualcommSerial(_devicePath!);
                _firehoseClient = new QualcommFirehose(_serialPort);

                FlushForResponse();

                _currentMode = DeviceMode.Firehose;
                break;
            case DeviceMode.Unknown:
            case DeviceMode.Error:
            default:
                throw new TodoException($"Cannot proceed: Device mode is {mode} or could not be determined.");
        }
    }

    public void FlushForResponse()
    {
        var GotResponse = false;
        try
        {
            while (!GotResponse)
            {
                var datas = _firehoseClient?.GetFirehoseResponseDataPayloads();

                if (datas == null)
                {
                    continue;
                }

                foreach (var data in datas)
                {
                    if (data.Log != null)
                    {
                        Logging.Log("DEVPRG LOG: " + data.Log.Value, LogLevel.Debug);
                    }
                    else if (data.Response != null)
                    {
                        GotResponse = true;
                    }
                }
            }
        }
        catch (BadConnectionException) { }
        catch (TimeoutException) { }
    }

    /// <summary>
    /// Connects via Sahara and uploads the specified Firehose programmer.
    /// </summary>
    public async Task UploadLoaderViaSaharaAsync()
    {
        if (string.IsNullOrEmpty(_globalOptions.LoaderPath))
        {
            throw new ArgumentException("No loader (--loader) specified, and auto-detection not implemented yet.");
        }
        if (!File.Exists(_globalOptions.LoaderPath))
        {
            throw new FileNotFoundException($"Loader file not found: {_globalOptions.LoaderPath}");
        }

        if (_saharaClient == null || _serialPort == null)
        {
            Logging.Log("Sahara client not pre-established, creating new connection.", LogLevel.Debug);
            if (string.IsNullOrEmpty(_devicePath))
            {
                if (!FindDevice())
                {
                    throw new InvalidOperationException("Failed to find a suitable EDL device before Sahara upload.");
                }
            }

            _serialPort?.Dispose();
            _serialPort = new QualcommSerial(_devicePath!);
            _saharaClient = new QualcommSahara(_serialPort);
            _initialSaharaHelloPacket = null;
        }
        else
        {
            Logging.Log("Using pre-established Sahara connection.", LogLevel.Debug);
        }

        if (_deviceGuid == COMPortGuid && _initialSaharaHelloPacket == null) // Only if using COM port and not using pre-read packet
        {
            Logging.Log("Device is COM Port and no pre-read HELLO. Sending ResetStateMachine command to device...", LogLevel.Debug);
            try
            {
                var resetStateMachineCmd = QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.ResetStateMachine);
                _serialPort.SendData(resetStateMachineCmd);
                await Task.Delay(50);
            }
            catch (Exception rsmEx)
            {
                Logging.Log($"Failed to send ResetStateMachine for COM port: {rsmEx.Message}", LogLevel.Warning);
            }
        }

        try
        {
            Logging.Log("Attempting Sahara handshake...", LogLevel.Debug);
            if (!_saharaClient.CommandHandshake(_initialSaharaHelloPacket))
            {
                Logging.Log("Initial Sahara handshake failed, attempting reset and retry...", LogLevel.Warning);
                _initialSaharaHelloPacket = null;
                try
                {
                    _saharaClient.ResetSahara();
                    await Task.Delay(500);
                    if (!_saharaClient.CommandHandshake(null))
                    {
                        throw new TodoException("Sahara handshake failed even after reset.");
                    }
                    Logging.Log("Sahara handshake successful after reset.", LogLevel.Info);
                }
                catch (Exception resetEx)
                {
                    Logging.Log($"Sahara reset/retry failed: {resetEx.Message}", LogLevel.Error);
                    throw new TodoException("Sahara handshake failed.");
                }
            }
            else
            {
                Logging.Log("Sahara handshake successful.", LogLevel.Debug);
            }
            _initialSaharaHelloPacket = null;

            var deviceVersion = _saharaClient.DetectedDeviceSaharaVersion;

            try
            {
                var sn = _saharaClient.GetSerialNumber();
                Logging.Log($"Serial Number: {Convert.ToHexString(sn)}", LogLevel.Info);
                if (deviceVersion < 3)
                {
                    Logging.Log("Sahara version < 3, attempting to get HWID and RKH.", LogLevel.Debug);
                    var hwid = _saharaClient.GetHWID();
                    Qualcomm.EmergencyDownload.ChipInfo.HardwareID.ParseHWID(hwid);
                }
                else
                {
                    Logging.Log("Sahara version >= 3, skipping HWID retrieval.", LogLevel.Debug);
                }

                var rkhs = _saharaClient.GetRKHs();
                for (var i = 0; i < rkhs.Length; i++)
                {
                    Logging.Log($"RKH[{i}]: {Convert.ToHexString(rkhs[i])}", LogLevel.Debug);
                }
            }
            catch (Exception ex)
            {
                Logging.Log($"Failed to get device info via Sahara: {ex.Message}", LogLevel.Warning);
            }

            Logging.Log("Switching to image transfer mode...", LogLevel.Debug);
            _saharaClient.SwitchMode(QualcommSaharaMode.ImageTXPending);
            await Task.Delay(100);

            Logging.Log($"Uploading loader: {_globalOptions.LoaderPath}", LogLevel.Info);

            var success = await Task.Run(() => _saharaClient.LoadProgrammer(_globalOptions.LoaderPath));

            if (!success)
            {
                throw new TodoException("Failed to upload programmer via Sahara.");
            }
            Logging.Log("Loader uploaded and started successfully via Sahara.", LogLevel.Info);
        }
        catch (Exception ex)
        {
            Logging.Log($"Error during Sahara operations: {ex.Message}", LogLevel.Error);
            Logging.Log(ex.ToString(), LogLevel.Debug);
            throw;
        }
        finally
        {
            Logging.Log("Closing Sahara connection after loader upload attempt.", LogLevel.Debug);
            _serialPort?.Close();
            _serialPort = null;
            _saharaClient = null;
        }
    }

    /// <summary>
    /// Sends the initial Firehose configuration command.
    /// </summary>
    public async Task ConfigureFirehoseAsync()
    {
        if (_firehoseClient == null) throw new InvalidOperationException("Not in Firehose mode.");

        try
        {
            Logging.Log("Sending Firehose configure command...", LogLevel.Info);

            var storage = _globalOptions.MemoryType ?? StorageType.UFS;
            var maxPayload = _globalOptions.MaxPayloadSize ?? 1048576;

            var success = await Task.Run(() => _firehoseClient.Configure(storage));

            if (!success)
            {
                // The Configure method in the provided QCEDL.NET doesn't seem to return false,
                // it relies on exceptions or log parsing. We might need to adjust it or add checks here.
                Logging.Log("Firehose configuration might have failed (check logs).", LogLevel.Warning);
            }
            Logging.Log($"Firehose configured for Memory: {storage}, MaxPayload: {maxPayload}\n", LogLevel.Info);
        }
        catch (Exception ex)
        {
            Logging.Log($"Error during Firehose configuration: {ex.Message}", LogLevel.Error);
            Logging.Log(ex.ToString(), LogLevel.Debug);
            throw;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _serialPort?.Dispose();
            }

            _serialPort = null;
            _saharaClient = null;
            _firehoseClient = null;
            _devicePath = null;

            _disposed = true;
        }
    }
}