using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using LibUsbDotNet.Main;
using Microsoft.Extensions.Logging;
using QCEDL.CLI.Logging;
using QCEDL.NET.Todo;
using QCEDL.NET.USB;
using Qualcomm.EmergencyDownload.ChipInfo;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;
using Qualcomm.EmergencyDownload.Layers.PBL.Sahara;
using Qualcomm.EmergencyDownload.Transport;

namespace QCEDL.CLI.Core;

internal sealed class EdlManager(
    ILogger<EdlManager> logger,
    GlobalOptionsBinder globalOptions) : IEdlManager
{
    private string? _devicePath;
    private Guid? _deviceGuid;
    private QualcommSerial? _serialPort;
    private QualcommSahara? _saharaClient;
    private QualcommFirehose? _firehoseClient;
    private bool _disposed;

    // GUIDs for device detection on Windows
    private static readonly Guid ComPortGuid = new("{86E0D1E0-8089-11D0-9CE4-08003E301F73}");
    private static readonly Guid WinUsbGuid = new("{a5dcbf10-6530-11d2-901f-00c04fb951ed}");

    private DeviceMode _currentMode = DeviceMode.Unknown;
    private byte[]? _initialSaharaHelloPacket;

    public DeviceMode CurrentMode => _currentMode;

    public QualcommFirehose Firehose =>
        _firehoseClient ?? throw new InvalidOperationException("Not connected in Firehose mode.");

    public bool IsFirehoseMode => _firehoseClient != null;

    /// <summary>
    /// Attempts to detect the current operating mode of the connected EDL device.
    /// Connects temporarily if not already connected.
    /// </summary>
    /// <returns>The detected DeviceMode.</returns>
    public async Task<DeviceMode> DetectCurrentModeAsync(bool forceReconnect = false)
    {
        if (_currentMode != DeviceMode.Unknown && _serialPort != null && !forceReconnect)
        {
            logger.UsingCachedDeviceMode(_currentMode);
            return _currentMode;
        }

        if (string.IsNullOrEmpty(_devicePath))
        {
            if (!FindDevice())
            {
                logger.CannotDetectModeNoDeviceFound();
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

        logger.ProbingDeviceMode();
        QualcommSerial? probeSerial = null;
        var detectedMode = DeviceMode.Unknown;

        try
        {
            probeSerial = new(_devicePath!);
            probeSerial.SetTimeOut(500); // Short timeout for initial read attempt
            // --- Probe 1: Passive Read ---
            logger.AttemptingPassiveRead();
            byte[]? initialReadBuffer;

            try
            {
                // Try reading a small amount.
                logger.ReadingInitialData();
                initialReadBuffer = probeSerial.GetResponse(null, 48); // Read up to 48 bytes raw
                logger.InitialReadCompleted();
            }
            catch (TimeoutException)
            {
                logger.PassiveReadReceivedIncompleteBadMessage();
                initialReadBuffer = null;
            }
            catch (BadMessageException)
            {
                logger.PassiveReadReceivedPotentiallyIncompleteBadMessage();
                initialReadBuffer = null;
            }
            catch (Exception ex)
            {
                logger.UnexceptedException(ex);
                initialReadBuffer = null;
            }

            if (initialReadBuffer != null && initialReadBuffer.Length > 0)
            {
                logger.PassiveReadGotBytes(initialReadBuffer.Length, Convert.ToHexString(initialReadBuffer));
                // Check for Sahara HELLO (starts with 0x01 0x00 0x00 0x00)
                if (initialReadBuffer is [0x01, 0x00, 0x00, 0x00, ..])
                {
                    logger.PassiveReadMatchesSaharaHelloPattern();
                    detectedMode = DeviceMode.Sahara;
                    _initialSaharaHelloPacket = initialReadBuffer;

                    _serialPort = probeSerial; // Keep the probeSerial as the main _serialPort
                    probeSerial = null; // Nullify probeSerial so it's not disposed in finally if kept
                    _saharaClient = new(_serialPort);
                    _currentMode = DeviceMode.Sahara;
                }
                // Check for Firehose XML start
                else if (Encoding.UTF8.GetString(initialReadBuffer).Contains("<?xml"))
                {
                    logger.PassiveReadContainsXmlDeclaration();
                    detectedMode = DeviceMode.Firehose;
                }
                else
                {
                    logger.PassiveReadReceivedUnexpectedData();
                }
            }

            // --- Probe 2: Active Firehose NOP (If Passive Read Failed/Inconclusive) ---
            if (detectedMode == DeviceMode.Unknown)
            {
                var serialForFirehoseProbe = _serialPort ?? probeSerial;
                if (serialForFirehoseProbe == null)
                {
                    logger.NoSerialPortAvailableForFirehoseNopProbe();
                }
                else
                {
                    logger.PassiveReadInconclusiveAttemptingActiveFirehoseNopProbe();
                    try
                    {
                        serialForFirehoseProbe.SetTimeOut(1500);
                        var firehoseProbe = new QualcommFirehose(serialForFirehoseProbe);
                        var nopCommand = QualcommFirehoseXml.BuildCommandPacket([new() { Nop = new() }]);
                        firehoseProbe.Serial.SendData(Encoding.UTF8.GetBytes(nopCommand));
                        var datas = await Task.Run(() => firehoseProbe.GetFirehoseResponseDataPayloads());
                        if (datas.Length > 0)
                        {
                            logger.FirehoseNopProbeSuccessful();
                            detectedMode = DeviceMode.Firehose;

                            logger.FlushingSerialOutput();
                            // Logging.Log("Reading initial data from device...", LogLevel.Debug);
                            // FlushForResponse();
                            var gotResponse = false;
                            try
                            {
                                while (!gotResponse)
                                {
                                    var flushingDatas = firehoseProbe.GetFirehoseResponseDataPayloads();

                                    foreach (var data in flushingDatas)
                                    {
                                        if (data.Log is not null)
                                        {
                                            logger.DevprgLog(data.Log.Value);
                                        }
                                        else if (data.Response != null)
                                        {
                                            gotResponse = true;
                                        }
                                    }
                                }
                            }
                            catch (BadConnectionException) { }
                        }
                        else
                        {
                            logger.FirehoseNopProbeNoXmlResponseReceived();
                        }
                    }
                    catch (TimeoutException ex)
                    {
                        logger.FirehoseNopProbeTimedOut(ex);
                    }
                    catch (BadConnectionException ex)
                    {
                        logger.FirehoseNopProbeFailedBadConnection(ex);
                    }
                    catch (BadMessageException ex)
                    {
                        logger.FirehoseNopProbeFailedBadMessage(ex);
                    }
                    catch (Exception ex)
                    {
                        logger.FirehoseNopProbeFailedUnexpectedly(ex);
                    }
                }
            }

            // --- Probe 3: Active Sahara Handshake (If Still Unknown) ---
            if (detectedMode == DeviceMode.Unknown)
            {
                var serialForSaharaProbe = _serialPort ?? probeSerial;
                if (serialForSaharaProbe == null)
                {
                    logger.NoSerialPortAvailable();
                }
                else
                {
                    logger.ProbesInconclusiveAttemptingFullHandshake();
                    try
                    {
                        serialForSaharaProbe.SetTimeOut(2000);
                        var saharaProbeClient = new QualcommSahara(serialForSaharaProbe);
                        // Pass null to CommandHandshake as we don't have a pre-read packet here
                        if (await Task.Run(() => saharaProbeClient.CommandHandshake()))
                        {
                            logger.FullSaharaHandshakeProbeSuccessfulDetectedMode();
                            detectedMode = DeviceMode.Sahara;
                            // If successful, this becomes the main connection
                            _serialPort = serialForSaharaProbe;
                            probeSerial = null; // Don't dispose it
                            _saharaClient = saharaProbeClient;
                            _currentMode = DeviceMode.Sahara;
                            try { _saharaClient.ResetSahara(); }
                            catch
                            {
                                /* Ignore reset errors */
                            }
                        }
                        else
                        {
                            logger.FullSaharaHandshakeProbeFailed();
                        }
                    }
                    catch (TimeoutException ex)
                    {
                        logger.FullSaharaHandshakeProbeTimedOut(ex);
                    }
                    catch (BadConnectionException ex)
                    {
                        logger.FullSaharaHandshakeProbeFailedBadConnection(ex);
                    }
                    catch (BadMessageException ex)
                    {
                        logger.FullSaharaHandshakeProbeFailedBadMessageLikelyNotSahara(ex);
                    }
                    catch (Exception ex)
                    {
                        logger.FullSaharaHandshakeProbeFailedUnexpectedly(ex);
                    }
                }
            }
            // --- Probe 4: Add streaming probe if needed ---
        }
        catch (Exception ex)
        {
            logger.ErrorDuringModeDetection(ex);
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

        logger.DetectedDeviceMode(_currentMode);
        return _currentMode;
    }

    /// <summary>
    /// Finds a compatible EDL device.
    /// </summary>
    /// <returns>True if a device is found, false otherwise.</returns>
    /// <exception cref="Exception">Throws if multiple devices are found without specific selection criteria.</exception>
    private bool FindDevice()
    {
        logger.SearchingForQualcommEdlDevice();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (FindDevicePosixLibUsb())
            {
                logger.FoundDeviceUsingLibUsbDotNetOnLinuxOrMacOS();
                return true;
            }

            // Fallback to Serial Port on Linux
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return false;
            }

            logger.LibUsbDotNetDeviceDetectionFailedFallbackToSerialPortOnLinux();
            logger.SerialPortKnownBrokenOnLinuxPleaseDoubleCheckWhyLibUsbNotWorkingIfDetectionSucceedsWithSerialPort();

            return FindDeviceLinuxSerial();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return FindDeviceWindows();
        }

        logger.UnsupportedOsDeviceDiscoverySkipped(RuntimeInformation.OSDescription);
        throw new PlatformNotSupportedException();
    }


    [SupportedOSPlatform("Linux")]
    [SupportedOSPlatform("macOS")]
    private bool FindDevicePosixLibUsb()
    {
        logger.TryingToFindDeviceUsingLibUsbDotNetOnLinuxOrMacOS();
        if (QualcommSerial.LibUsbContext == null)
        {
            logger.LibUsbDotNetContextNotInitializedCannotUseLibUsbBackend();
            return false;
        }

        var vidToFind = globalOptions.Vid ?? 0x05C6;
        var pidToFind = globalOptions.Pid ?? 0x9008;
        // string serialToFind = globalOptions.SerialNumber; // If you add a serial number option
        try
        {
            var finder = new UsbDeviceFinder { Vid = vidToFind, Pid = pidToFind };
            // if (!string.IsNullOrEmpty(serialToFind)) finder.SerialNumber = serialToFind;

            var usbDevice = QualcommSerial.LibUsbContext.Find(finder);
            if (usbDevice != null)
            {
                // _libUsbSerialNumber = serialToFind; // If used
                _devicePath = $"usb:vid_{vidToFind:X4},pid_{pidToFind:X4}";
                _deviceGuid = WinUsbGuid; // Use WinUSBGuid to signify LibUsbDotNet backend to QualcommSerial
                logger.LibUsbDotNetFoundDevice(vidToFind, pidToFind, _devicePath);
                return true;
            }

            logger.LibUsbDotNetNoDeviceFound(vidToFind, pidToFind);
            return false;
        }
        catch (Exception ex)
        {
            logger.ErrorDuringLibUsbDotNetDeviceDiscoveryOnLinux(ex);
            return false;
        }
    }

    [SupportedOSPlatform("Linux")]
    private bool FindDeviceLinuxSerial()
    {
        logger.SearchingForQualcommEdlDeviceOnLinuxOrMacOS();
        var targetVid = (globalOptions.Vid ?? 0x05C6).ToString("x4");
        var targetPid = (globalOptions.Pid ?? 0x9008).ToString("x4");
        logger.TargetVidPid(targetVid, targetPid);
        var potentialTtyPaths = new List<string>();
        try
        {
            var sysTtyPath = "/sys/class/tty";
            if (!Directory.Exists(sysTtyPath))
            {
                logger.SysfsTtyPathNotFound(sysTtyPath);
                return false;
            }

            foreach (var dirName in Directory.GetDirectories(sysTtyPath))
            {
                var ttyName = Path.GetFileName(dirName);
                if (ttyName.StartsWith("ttyUSB") || ttyName.StartsWith("ttyACM"))
                {
                    logger.TtyDirName(dirName);
                    var realDevicePath = new FileInfo(dirName).ResolveLinkTarget(true)?.FullName;
                    if (realDevicePath == null)
                    {
                        continue;
                    }

                    logger.FoundTtyRealDevicePath(ttyName, realDevicePath);

                    var ttyDir = Directory.GetParent(realDevicePath);

                    var usbDeviceDir = ttyDir?.Parent?.Parent;
                    if (usbDeviceDir == null)
                    {
                        continue;
                    }

                    if (ttyName.StartsWith("ttyUSB"))
                    {
                        // Another level up
                        usbDeviceDir = usbDeviceDir.Parent;
                        if (usbDeviceDir == null)
                        {
                            continue;
                        }
                    }

                    var vidPath = Path.Combine(usbDeviceDir.FullName, "idVendor");
                    var pidPath = Path.Combine(usbDeviceDir.FullName, "idProduct");
                    if (File.Exists(vidPath) && File.Exists(pidPath))
                    {
                        var vid = File.ReadAllText(vidPath).Trim();
                        var pid = File.ReadAllText(pidPath).Trim();

                        logger.FoundTtyVidPid(ttyName, vid, pid);
                        if (vid.Equals(targetVid, StringComparison.OrdinalIgnoreCase) &&
                            pid.Equals(targetPid, StringComparison.OrdinalIgnoreCase))
                        {
                            var devPath = Path.Combine("/dev", ttyName);
                            if (File.Exists(devPath)) // Check if /dev/ttyUSBx actually exists
                            {
                                potentialTtyPaths.Add(devPath);
                                logger.MatchForVidPid(devPath, targetVid, targetPid);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.ErrorDuringLinuxDeviceDiscovery(ex);
            return false;
        }

        if (potentialTtyPaths.Count == 0)
        {
            logger.NoMatchingSerialDevicesFound();
            return false;
        }

        if (potentialTtyPaths.Count > 1)
        {
            logger.MultipleMatchingSerialDevicesFound(potentialTtyPaths.Count, potentialTtyPaths[0]);
            foreach (var p in potentialTtyPaths)
            {
                logger.FoundDeviceListItem(p);
            }
        }

        _devicePath = potentialTtyPaths[0];
        _deviceGuid = ComPortGuid; // For some relevant logic
        logger.SelectedDevice(_devicePath);
        logger.ModeDetectedSerialPort();
        return true;
    }

    [SupportedOSPlatform("Windows")]
    private bool FindDeviceWindows()
    {
        logger.SearchingForQualcommEdlDeviceOnWindows();

        // Store DevInst and the interface GUID it was found with
        var potentialDevices = new List<(string DevicePath, string BusName, Guid InterfaceGuid, int DevInst)>();
        // Search by COMPortGuid
        foreach (var deviceInfo in USBExtensions.GetDeviceInfos(ComPortGuid))
        {
            logger.FoundDeviceViaComPortGuid(deviceInfo.PathName, deviceInfo.BusName, deviceInfo.DevInst);
            if (deviceInfo.PathName is not null && IsQualcommEdlDevice(deviceInfo.PathName, deviceInfo.BusName))
            {
                potentialDevices.Add((deviceInfo.PathName, deviceInfo.BusName, ComPortGuid, deviceInfo.DevInst));
            }
        }

        // Search by WinUSBGuid
        foreach (var deviceInfo in USBExtensions.GetDeviceInfos(WinUsbGuid))
        {
            logger.FoundDeviceViaWinUsbGuid(deviceInfo.PathName, deviceInfo.BusName, deviceInfo.DevInst);
            if (deviceInfo.PathName is not null && IsQualcommEdlDevice(deviceInfo.PathName, deviceInfo.BusName))
            {
                potentialDevices.Add((deviceInfo.PathName, deviceInfo.BusName, WinUsbGuid, deviceInfo.DevInst));
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
                var preferredDevice = g.FirstOrDefault(item => item.InterfaceGuid == ComPortGuid);
                if (preferredDevice.DevicePath != null)
                {
                    logger.SelectingDeviceViaComPortGuid(preferredDevice.DevInst, preferredDevice.DevicePath);
                    return preferredDevice;
                }

                var fallbackDevice = g.First();
                logger.SelectingDeviceViaInterface(fallbackDevice.DevInst,
                    fallbackDevice.InterfaceGuid == WinUsbGuid ? "WinUSBGuid" : "OtherGuid", fallbackDevice.DevicePath);
                return fallbackDevice;
            })
            .ToList();
        if (uniqueEdlDevices.Count == 0)
        {
            logger.NoQualcommEdlDevicesFound();
            return false;
        }

        if (uniqueEdlDevices.Count > 1)
        {
            logger.MultipleUniqueEdlDevicesFound(uniqueEdlDevices.Count);

            foreach (var dev in uniqueEdlDevices)
            {
                logger.UniqueEdlDeviceListItem(dev.DevicePath, dev.BusName,
                    dev.InterfaceGuid == ComPortGuid ? "COM Port" : "WinUSB");
            }

            // For simplicity, pick the first one if multiple are found, or require user to specify.
            // return false; // Or handle selection
            logger.PickingFirstDevice(uniqueEdlDevices[0].DevicePath);
        }

        var selectedDevice = uniqueEdlDevices.First();
        logger.QualcommEdlDeviceSelected(selectedDevice.DevicePath, selectedDevice.BusName,
            selectedDevice.InterfaceGuid == ComPortGuid ? "COM Port" : "WinUSB", selectedDevice.DevInst);

        _devicePath = selectedDevice.DevicePath;
        _deviceGuid = selectedDevice.InterfaceGuid;

        logger.FoundDevice(_devicePath);
        logger.FoundDeviceInterface(_deviceGuid == ComPortGuid ? "Serial Port" : "libusb via WinUSB");
        logger.FoundDeviceBusName(selectedDevice.BusName);

        if (selectedDevice.BusName.StartsWith("QUSB_BULK", StringComparison.OrdinalIgnoreCase) ||
            selectedDevice.BusName == "QHSUSB_DLOAD" ||
            selectedDevice.BusName == "QHSUSB__BULK")
        {
            logger.ModeDetectedSaharaFirehose();
        }
        else if (selectedDevice.BusName == "QHSUSB_ARMPRG")
        {
            logger.ModeDetectedEmergencyFlash();
        }
        else
        {
            logger.ModeDetectionBasedOnBusNameUncertain();
        }

        return true;
    }

    private bool IsQualcommEdlDevice(string devicePath, string busName)
    {
        var isQualcomm = devicePath.Contains("VID_05C6&", StringComparison.OrdinalIgnoreCase);
        if (globalOptions.Vid is not null && isQualcomm)
        {
            isQualcomm = devicePath.Contains($"VID_{globalOptions.Vid.Value:X4}&", StringComparison.OrdinalIgnoreCase);
        }

        var isEdl = devicePath.Contains("&PID_9008", StringComparison.OrdinalIgnoreCase);
        if (globalOptions.Pid is not null && isEdl)
        {
            isEdl = devicePath.Contains($"&PID_{globalOptions.Pid.Value:X4}", StringComparison.OrdinalIgnoreCase);
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
            logger.AlreadyInFirehoseMode();
            return;
        }

        var mode = await DetectCurrentModeAsync(forceReconnect: true);
        switch (mode)
        {
            case DeviceMode.Firehose:
                logger.DeviceInFirehoseEstablishingConnection();
                _serialPort?.Dispose();
                _serialPort = new(_devicePath!);
                _firehoseClient = new(_serialPort);
                break;
            case DeviceMode.Sahara:
                logger.DeviceInSaharaUploadingLoader();
                await UploadLoaderViaSaharaAsync();
                logger.WaitingForReenumerationInFirehose();
                await Task.Delay(500);

                // Clear old path/state and find the device again
                _devicePath = null;
                _serialPort = null;
                _firehoseClient = null;
                _saharaClient = null;
                _currentMode = DeviceMode.Unknown;
                if (!FindDevice()) // Find the potentially new device path
                {
                    throw new TodoException(
                        "Device did not re-enumerate in Firehose mode after loader upload, or could not be found.");
                }

                // Now establish the Firehose connection
                logger.ConnectingToReenumeratedFirehose();
                _serialPort = new(_devicePath!);
                _firehoseClient = new(_serialPort);

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
        var gotResponse = false;
        try
        {
            while (!gotResponse)
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
                        logger.DevprgLog(data.Log.Value);
                    }
                    else if (data.Response != null)
                    {
                        gotResponse = true;
                    }
                }
            }
        }
        catch (BadConnectionException)
        {
        }
        catch (TimeoutException)
        {
        }
    }

    /// <summary>
    /// Connects via Sahara and uploads the specified Firehose programmer.
    /// </summary>
    public async Task UploadLoaderViaSaharaAsync()
    {
        if (string.IsNullOrEmpty(globalOptions.LoaderPath))
        {
            throw new ArgumentException("No loader (--loader) specified, and auto-detection not implemented yet.");
        }

        if (!File.Exists(globalOptions.LoaderPath))
        {
            throw new FileNotFoundException($"Loader file not found: {globalOptions.LoaderPath}");
        }

        if (_saharaClient == null || _serialPort == null)
        {
            logger.SaharaClientNotPreEstablishedCreatingNewConnection();
            if (string.IsNullOrEmpty(_devicePath))
            {
                if (!FindDevice())
                {
                    throw new InvalidOperationException("Failed to find a suitable EDL device before Sahara upload.");
                }
            }

            _serialPort?.Dispose();
            _serialPort = new(_devicePath!);
            _saharaClient = new(_serialPort);
            _initialSaharaHelloPacket = null;
        }
        else
        {
            logger.UsingPreEstablishedSaharaConnection();
        }

        if (_deviceGuid == ComPortGuid &&
            _initialSaharaHelloPacket == null) // Only if using COM port and not using pre-read packet
        {
            logger.DeviceIsComPortNoPreReadHelloSendingResetStateMachineCommandToDevice();
            try
            {
                var resetStateMachineCmd = QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.ResetStateMachine);
                _serialPort.SendData(resetStateMachineCmd);
                await Task.Delay(50);
            }
            catch (Exception rsmEx)
            {
                logger.FailedToSendResetStateMachineForComPort(rsmEx);
            }
        }

        try
        {
            logger.AttemptingSaharaHandshake();
            if (!_saharaClient.CommandHandshake(_initialSaharaHelloPacket))
            {
                logger.InitialSaharaHandshakeFailedAttemptingResetAndRetry();
                _initialSaharaHelloPacket = null;
                try
                {
                    _saharaClient.ResetSahara();
                    await Task.Delay(500);
                    if (!_saharaClient.CommandHandshake())
                    {
                        // TODO: throw here will be catch after
                        throw new TodoException("Sahara handshake failed even after reset.");
                    }

                    logger.SaharaHandshakeSuccessfulAfterReset();
                }
                catch (Exception resetEx)
                {
                    logger.SaharaResetRetryFailed(resetEx);
                    throw new TodoException("Sahara handshake failed.");
                }
            }
            else
            {
                logger.SaharaHandshakeSuccessful();
            }

            _initialSaharaHelloPacket = null;

            var deviceVersion = _saharaClient.DetectedDeviceSaharaVersion;

            try
            {
                var sn = _saharaClient.GetSerialNumber();
                logger.SerialNumber(Convert.ToHexString(sn));
                if (deviceVersion < 3)
                {
                    logger.SaharaVersionLessThan3AttemptingGetHwidAndRkh();
                    var hwid = _saharaClient.GetHWID();
                    HardwareID.ParseHWID(hwid);
                }
                else
                {
                    logger.SaharaVersionGreaterOrEqual3SkippingHwidRetrieval();
                }

                var rkhs = _saharaClient.GetRKHs();
                for (var i = 0; i < rkhs.Length; i++)
                {
                    logger.RkhAtIndex(i, Convert.ToHexString(rkhs[i]));
                }
            }
            catch (Exception ex)
            {
                logger.FailedToGetDeviceInfoViaSahara(ex);
            }

            logger.SwitchingToImageTransferMode();
            _saharaClient.SwitchMode(QualcommSaharaMode.ImageTXPending);
            await Task.Delay(100);

            logger.UploadingLoader(globalOptions.LoaderPath);

            var success = await Task.Run(() => _saharaClient.LoadProgrammer(globalOptions.LoaderPath));

            if (!success)
            {
                throw new TodoException("Failed to upload programmer via Sahara.");
            }

            logger.LoaderUploadedAndStartedSuccessfullyViaSahara();
        }
        catch (Exception ex)
        {
            logger.ErrorDuringSaharaOperations(ex);
            throw;
        }
        finally
        {
            logger.ClosingSaharaConnectionAfterLoaderUploadAttempt();
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
        if (_firehoseClient == null)
        {
            throw new InvalidOperationException("Not in Firehose mode.");
        }

        try
        {
            logger.SendingFirehoseConfigureCommand();

            var storage = globalOptions.MemoryType ?? StorageType.UFS;
            var maxPayload = globalOptions.MaxPayloadSize ?? 1048576;

            var success = await Task.Run(() => _firehoseClient.Configure(storage));

            if (!success)
            {
                // The Configure method in the provided QCEDL.NET doesn't seem to return false,
                // it relies on exceptions or log parsing. We might need to adjust it or add checks here.
                logger.FirehoseConfigurationMightHaveFailedCheckLogs();
            }

            logger.FirehoseConfiguredForMemoryMaxPayload(storage, maxPayload);
        }
        catch (Exception ex)
        {
            logger.ErrorDuringFirehoseConfiguration(ex);
            throw;
        }
    }

    public void Dispose()
    {
        Dispose(true);
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _serialPort?.Dispose();
            }

            _disposed = true;
        }
    }
}