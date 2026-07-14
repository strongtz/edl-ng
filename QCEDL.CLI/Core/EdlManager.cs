using System.Runtime.InteropServices;
using System.Text;
using LibUsbDotNet.Main;
using QCEDL.CLI.Helpers;
using QCEDL.NET.PartitionTable;
using QCEDL.NET.Todo;
using QCEDL.NET.USB;
using Qualcomm.EmergencyDownload.ChipInfo;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.JSON.StorageInfo;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;
using Qualcomm.EmergencyDownload.Layers.PBL.Sahara;
using Qualcomm.EmergencyDownload.Transport;

namespace QCEDL.CLI.Core;

internal sealed class EdlManager(GlobalOptionsBinder globalOptions) : IDisposable
{
    private string? _devicePath;
    private Guid? _deviceGuid;
    private IQualcommTransport? _transport;
    private QualcommSahara? _saharaClient;
    private QualcommFirehose? _firehoseClient;
    internal IStorageBackend? XstorageBackend;
    private bool _firehoseConfigured;
    private bool _disposed;

    // GUIDs for device detection on Windows
    private static readonly Guid ComPortGuid = new("{86E0D1E0-8089-11D0-9CE4-08003E301F73}");
    private static readonly Guid WinUsbGuid = new("{a5dcbf10-6530-11d2-901f-00c04fb951ed}");

    // Default USB device IDs
    private static readonly int DefaultVid = 0x05C6;
    private static readonly int[] DefaultPids = [0x9008, 0x900E];

    private byte[]? _initialSaharaHelloPacket;

    public DeviceMode CurrentMode { get; private set; }

    public QualcommFirehose Firehose => _firehoseClient ?? throw new InvalidOperationException("Not connected in Firehose mode.");
    public bool IsFirehoseMode => _firehoseClient != null;

    public bool IsHostDeviceMode => !string.IsNullOrEmpty(globalOptions.HostDevAsTarget);
    public bool IsRadxaWosMode => globalOptions.RadxaWosPlatform;
    public bool IsDirectMode => IsHostDeviceMode || IsRadxaWosMode;

    public string GetTargetDescription(uint lun)
    {
        return IsHostDeviceMode
            ? "host device"
            : IsRadxaWosMode
                ? "Radxa WoS platform"
                : $"LUN {lun}";
    }

    private HostDeviceManager? _hostDeviceManager;
    private RadxaWoSDeviceManager? _radxaWoSManager;

    private GlobalOptionsBinder GlobalOptions => globalOptions;

    private void EnsureValidDirectMode()
    {
        if (IsHostDeviceMode && IsRadxaWosMode)
        {
            throw new InvalidOperationException("Cannot use --hostdev-as-target and --radxa-wos-platform at the same time.");
        }
    }

    public HostDeviceManager GetHostDeviceManager()
    {
        EnsureValidDirectMode();

        if (!IsHostDeviceMode)
        {
            throw new InvalidOperationException("Not in host device mode. Use --hostdev-as-target to enable this mode.");
        }

        _hostDeviceManager ??= new HostDeviceManager(globalOptions.HostDevAsTarget!, globalOptions.ImgSize);

        return _hostDeviceManager;
    }

    private RadxaWoSDeviceManager GetRadxaWoSDeviceManager()
    {
        EnsureValidDirectMode();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException("Radxa WoS backend is only available on Windows.");
        }

        if (!IsRadxaWosMode)
        {
            throw new InvalidOperationException("Radxa WoS backend is not enabled. Use --radxa-wos-platform to enable it.");
        }

        _radxaWoSManager ??= new RadxaWoSDeviceManager();
        return _radxaWoSManager;
    }

    internal IStorageBackend StorageBackend => XstorageBackend ??= CreateStorageBackend();

    internal IStorageBackend CreateStorageBackend()
    {
        EnsureValidDirectMode();

        return IsHostDeviceMode
            ? new HostStorageBackend(GetHostDeviceManager())
            : IsRadxaWosMode
                ? new RadxaWoSBackend(GetRadxaWoSDeviceManager())
                : new FirehoseStorageBackend(this);
    }

    public async Task<StorageGeometry> GetStorageGeometryAsync(uint lun)
    {
        return await StorageBackend.GetGeometryAsync(lun);
    }

    public async Task ReadSectorsToStreamAsync(uint lun, ulong startSector, ulong sectorCount, Stream destination, Action<long, long>? progressCallback = null)
    {
        await StorageBackend.ReadSectorsToStreamAsync(lun, startSector, sectorCount, destination, progressCallback);
    }

    public async Task ApplyPatchAsync(string startSectorStr, uint byteOffset, uint sizeInBytes, string valueStr, string filename)
    {
        if (IsHostDeviceMode || IsRadxaWosMode)
        {
            EnsureValidDirectMode();

            if (!string.Equals(filename, "DISK", StringComparison.OrdinalIgnoreCase))
            {
                Logging.Log($"Skipping patch with filename='{filename}' (only 'DISK' patches are processed in direct host mode)", LogLevel.Debug);
                return;
            }

            BlockDeviceManagerBase targetManager = IsHostDeviceMode
                ? GetHostDeviceManager()
                : GetRadxaWoSDeviceManager();

            await Task.Run(() => targetManager.ApplyPatch(startSectorStr, byteOffset, sizeInBytes, valueStr));
            return;
        }

        // In Firehose mode, patches are sent to the device as XML
        await EnsureFirehoseModeAsync();

        var patchXml = $"""
        <?xml version="1.0" ?>
        <data>
            <patch start_sector="{startSectorStr}" 
                   byte_offset="{byteOffset}" 
                   size_in_bytes="{sizeInBytes}" 
                   value="{valueStr}" 
                   filename="{filename}" 
                   SECTOR_SIZE_IN_BYTES="{GetSectorSize()}" 
                   physical_partition_number="{globalOptions.Slot}" />
        </data>
        """;

        var success = await Task.Run(() => Firehose.SendRawXmlAndGetResponse(patchXml));
        if (!success)
        {
            throw new InvalidOperationException($"Patch operation failed for sector {startSectorStr}");
        }
    }


    /// <summary>
    /// Attempts to detect the current operating mode of the connected EDL device.
    /// Connects temporarily if not already connected.
    /// </summary>
    /// <returns>The detected DeviceMode.</returns>
    public async Task<DeviceMode> DetectCurrentModeAsync(bool forceReconnect = false)
    {
        if (CurrentMode != DeviceMode.Unknown && _transport != null && !forceReconnect)
        {
            Logging.Log($"Using cached device mode: {CurrentMode}", LogLevel.Debug);
            return CurrentMode;
        }

        if (string.IsNullOrEmpty(_devicePath))
        {
            if (!FindDevice())
            {
                Logging.Log("Cannot detect mode: No device found.", LogLevel.Error);
                CurrentMode = DeviceMode.Error;
                return DeviceMode.Error;
            }
        }

        if (forceReconnect)
        {
            _transport?.Dispose();
            _transport = null;
            _saharaClient = null;
            _firehoseClient = null;
            CurrentMode = DeviceMode.Unknown;
            _initialSaharaHelloPacket = null;
            _firehoseConfigured = false;
        }

        Logging.Log("Probing device mode...", LogLevel.Debug);
        IQualcommTransport? probeTransport = null;
        var detectedMode = DeviceMode.Unknown;
        byte[]? initialReadBuffer = null;
        var passiveReadTimedOut = false;

        try
        {
            probeTransport = OpenTransport();
            probeTransport.TimeoutMilliseconds = 500; // Short timeout for initial read attempt
            // --- Probe 1: Passive Read ---
            Logging.Log("Attempting passive read...", LogLevel.Debug);
            try
            {
                // Try reading a small amount.
                Logging.Log("Reading initial data from device...", LogLevel.Debug);
                initialReadBuffer = probeTransport.GetResponse(null, 48); // Read up to 48 bytes raw
                Logging.Log("Initial read completed.", LogLevel.Debug);
            }
            catch (TimeoutException)
            {
                Logging.Log("Passive read timed out (no initial data from device).", LogLevel.Debug);
                initialReadBuffer = null;
                passiveReadTimedOut = true;
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
                    var saharaMode = (QualcommSaharaMode)ByteOperations.ReadUInt32(initialReadBuffer, 0x14); // 0x14 is the offset for Mode in HELLO packet
                    if (saharaMode == QualcommSaharaMode.MemoryDebug)
                    {
                        Logging.Log("Passive read indicates Sahara MemoryDebug (crashdump) mode.", LogLevel.Debug);
                        detectedMode = DeviceMode.SaharaMemoryDebug;
                    }
                    else
                    {
                        Logging.Log($"Passive read matches Sahara HELLO pattern (Mode: {saharaMode}). Detected Mode: Sahara", LogLevel.Debug);
                        detectedMode = DeviceMode.Sahara;
                    }

                    _initialSaharaHelloPacket = initialReadBuffer;

                    _transport = probeTransport;
                    probeTransport = null;
                    _saharaClient = new(_transport);
                    CurrentMode = detectedMode;

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

            // Modern Qualcomm USB serial drivers may discard the initial Sahara HELLO before
            // it reaches the application. Match qdl by speculatively responding immediately
            // after the first QUD read timeout, before sending any Firehose probe traffic.
            if (detectedMode == DeviceMode.Unknown && passiveReadTimedOut && probeTransport != null &&
                probeTransport.Backend == TransportBackend.WindowsQud)
            {
                Logging.Log("Initial Windows QUD read timed out. Attempting discarded HELLO recovery...", LogLevel.Debug);
                probeTransport.TimeoutMilliseconds = 2000;
                var saharaProbeClient = new QualcommSahara(probeTransport);
                var handshakeResult = await Task.Run(() =>
                    saharaProbeClient.ProbeCommandMode(initialReadAlreadyTimedOut: true));

                if (handshakeResult == QualcommSaharaHandshakeResult.Sahara)
                {
                    Logging.Log("Discarded HELLO recovery successful. Detected Mode: Sahara");
                    detectedMode = DeviceMode.Sahara;
                    _transport = probeTransport;
                    probeTransport = null;
                    _saharaClient = saharaProbeClient;
                    CurrentMode = DeviceMode.Sahara;
                    try { _saharaClient.ResetSahara(); } catch { /* Ignore reset errors */ }
                }
                else if (handshakeResult == QualcommSaharaHandshakeResult.Firehose)
                {
                    Logging.Log("Discarded HELLO recovery received Firehose XML. Detected Mode: Firehose");
                    detectedMode = DeviceMode.Firehose;
                }
                else
                {
                    Logging.Log("Discarded HELLO recovery was inconclusive.", LogLevel.Debug);
                }
            }

            // --- Probe 2: Active Firehose NOP (If Passive Read Failed/Inconclusive) ---
            if (detectedMode == DeviceMode.Unknown)
            {
                var transportForFirehoseProbe = _transport ?? probeTransport;
                if (transportForFirehoseProbe == null)
                {
                    Logging.Log("No transport available for Firehose NOP probe.", LogLevel.Error);
                }
                else
                {
                    Logging.Log("Passive read inconclusive. Attempting active Firehose NOP probe...", LogLevel.Debug);
                    try
                    {
                        transportForFirehoseProbe.TimeoutMilliseconds = 1500;
                        var firehoseProbe = new QualcommFirehose(transportForFirehoseProbe);
                        var nopCommand = QualcommFirehoseXml.BuildCommandPacket([new() { Nop = new() }]);
                        firehoseProbe.Transport.SendData(Encoding.UTF8.GetBytes(nopCommand));
                        var datas = await Task.Run(() => firehoseProbe.GetFirehoseResponseDataPayloads());
                        if (datas.Length > 0)
                        {
                            Logging.Log("Firehose NOP probe successful. Detected Mode: Firehose", LogLevel.Debug);
                            detectedMode = DeviceMode.Firehose;

                            Logging.Log("Flushing transport output...", LogLevel.Debug);
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
                                        if (data.Log != null)
                                        {
                                            Logging.Log("DEVPRG LOG: " + data.Log.Value, LogLevel.Debug);
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
                var transportForSaharaProbe = _transport ?? probeTransport;
                if (transportForSaharaProbe == null)
                {
                    Logging.Log("No transport available for Sahara handshake probe.", LogLevel.Error);
                }
                else
                {
                    Logging.Log("Probes inconclusive. Attempting *full* Sahara handshake as last resort...");
                    try
                    {
                        transportForSaharaProbe.TimeoutMilliseconds = 2000;
                        var saharaProbeClient = new QualcommSahara(transportForSaharaProbe);
                        // Pass null to CommandHandshake as we don't have a pre-read packet here
                        var handshakeResult = await Task.Run(() => saharaProbeClient.ProbeCommandMode());
                        if (handshakeResult == QualcommSaharaHandshakeResult.Sahara)
                        {
                            Logging.Log("Full Sahara handshake probe successful. Detected Mode: Sahara");
                            detectedMode = DeviceMode.Sahara;
                            // If successful, this becomes the main connection
                            _transport = transportForSaharaProbe;
                            probeTransport = null; // Don't dispose it
                            _saharaClient = saharaProbeClient;
                            CurrentMode = DeviceMode.Sahara;
                            try { _saharaClient.ResetSahara(); } catch { /* Ignore reset errors */ }
                        }
                        else if (handshakeResult == QualcommSaharaHandshakeResult.Firehose)
                        {
                            Logging.Log("Sahara probe received Firehose XML. Detected Mode: Firehose");
                            detectedMode = DeviceMode.Firehose;
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
            if (probeTransport != null && probeTransport != _transport)
            {
                probeTransport.Dispose();
            }
        }

        if (CurrentMode == DeviceMode.Unknown)
        {
            CurrentMode = detectedMode;
        }
        Logging.Log($"Detected device mode: {CurrentMode}");
        return CurrentMode;
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
                Logging.Log("Found device using LibUsbDotNet on Linux / MacOS.");
                return true;
            }
            return false;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return FindDeviceWindows();
        }

        Logging.Log($"Unsupported OS: {RuntimeInformation.OSDescription}. Device discovery skipped.", LogLevel.Error);
        return false;
    }

    private IQualcommTransport OpenTransport()
    {
        if (string.IsNullOrEmpty(_devicePath) || !_deviceGuid.HasValue)
        {
            throw new InvalidOperationException("No EDL transport target has been selected.");
        }

        var backend = _deviceGuid == ComPortGuid
            ? TransportBackend.WindowsQud
            : TransportBackend.LibUsb;
        return QualcommTransportFactory.Open(_devicePath, backend);
    }

    private bool FindDeviceLinuxLibUsb()
    {
        Logging.Log("Trying to find device using LibUsbDotNet on Linux / MacOS...", LogLevel.Debug);
        if (LibUsbTransport.Context == null)
        {
            Logging.Log("LibUsbDotNet context not initialized. Cannot use LibUsb backend.", LogLevel.Warning);
            return false;
        }

        var vidToFind = globalOptions.Vid ?? DefaultVid;
        var pidsToFind = globalOptions.Pid.HasValue ? [globalOptions.Pid.Value] : DefaultPids;

        // string serialToFind = _globalOptions.SerialNumber; // If you add a serial number option
        try
        {
            foreach (var pidToFind in pidsToFind)
            {
                var finder = new UsbDeviceFinder
                {
                    Vid = vidToFind,
                    Pid = pidToFind
                };
                // if (!string.IsNullOrEmpty(serialToFind)) finder.SerialNumber = serialToFind;

                var usbDevice = LibUsbTransport.Context.Find(finder);
                if (usbDevice != null)
                {
                    // _libUsbSerialNumber = serialToFind; // If used
                    _devicePath = $"usb:vid_{vidToFind:X4},pid_{pidToFind:X4}";
                    _deviceGuid = WinUsbGuid;
                    Logging.Log($"LibUsbDotNet found device: VID={vidToFind:X4}, PID={pidToFind:X4}. Path set to: {_devicePath}", LogLevel.Debug);
                    return true;
                }

                Logging.Log($"LibUsbDotNet: No device found with VID={vidToFind:X4}, PID={pidToFind:X4}.", LogLevel.Debug);
            }

            Logging.Log($"LibUsbDotNet: No device found with VID={vidToFind:X4} and any of the PIDs: {string.Join(", ", pidsToFind.Select(p => $"0x{p:X4}"))}.", LogLevel.Debug);
            return false;
        }
        catch (Exception ex)
        {
            Logging.Log($"Error during LibUsbDotNet device discovery on Linux: {ex.Message}", LogLevel.Error);
            Logging.Log(ex.ToString(), LogLevel.Debug);
            return false;
        }
    }

    private bool FindDeviceWindows()
    {
        Logging.Log("Searching for Qualcomm EDL device on Windows (Qualcomm Serial Driver or WinUSB)...");

        // Store DevInst and the interface GUID it was found with
        var potentialDevices =
            new List<(string DevicePath, string BusName, Guid InterfaceGuid, int DevInst, string OpenPath)>();
        // Search by COMPortGuid
        foreach (var (pathName, busName, devInst, portName) in UsbExtensions.GetDeviceInfos(ComPortGuid))
        {
            Logging.Log($"Found device via COMPortGuid: {pathName} on bus {busName} (DevInst: {devInst})", LogLevel.Debug);
            if (pathName is not null && !string.IsNullOrEmpty(portName) &&
                IsQualcommEdlDevice(pathName, busName))
            {
                potentialDevices.Add((pathName, busName, ComPortGuid, devInst, $@"\\.\{portName}"));
            }
        }
        // Search by WinUSBGuid
        foreach (var (pathName, busName, devInst, _) in UsbExtensions.GetDeviceInfos(WinUsbGuid))
        {
            Logging.Log($"Found device via WinUSBGuid: {pathName} on bus {busName} (DevInst: {devInst})", LogLevel.Debug);
            if (pathName is not null && IsQualcommEdlDevice(pathName, busName))
            {
                potentialDevices.Add((pathName, busName, WinUsbGuid, devInst, pathName));
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
                    Logging.Log($"Selecting device (DevInst: {preferredDevice.DevInst}) via COMPortGuid: {preferredDevice.DevicePath}", LogLevel.Debug);
                    return preferredDevice;
                }
                var fallbackDevice = g.First();
                Logging.Log($"Selecting device (DevInst: {fallbackDevice.DevInst}) via {(fallbackDevice.InterfaceGuid == WinUsbGuid ? "WinUSBGuid" : "OtherGuid")}: {fallbackDevice.DevicePath}", LogLevel.Debug);
                return fallbackDevice;
            })
            .ToList();
        if (uniqueEdlDevices.Count == 0)
        {
            Logging.Log("No Qualcomm EDL devices found.", LogLevel.Error);
            return false;
        }

        if (uniqueEdlDevices.Count > 1)
        {
            Logging.Log($"Multiple ({uniqueEdlDevices.Count}) unique EDL devices found. Please specify which device to use.", LogLevel.Error);
            foreach (var (devicePath, busName, interfaceGuid, _, _) in uniqueEdlDevices)
            {
                Logging.Log($"  - Path: {devicePath}, Bus: {busName}, Interface: {(interfaceGuid == ComPortGuid ? "COM Port" : "WinUSB")}", LogLevel.Error);
            }
            // For simplicity, pick the first one if multiple is found, or require user to specify.
            // return false; // Or handle selection
            Logging.Log($"Picking the first device: {uniqueEdlDevices[0].DevicePath}", LogLevel.Warning);
        }

        var (finalDevicePath, finalBusName, finalInterfaceGuid, finalDevInst, finalOpenPath) =
            uniqueEdlDevices.First();
        Logging.Log($"Qualcomm EDL device selected: {finalDevicePath} on bus {finalBusName} (Interface: {(finalInterfaceGuid == ComPortGuid ? "COM Port" : "WinUSB")}, DevInst: {finalDevInst})", LogLevel.Debug);

        _devicePath = finalOpenPath;
        _deviceGuid = finalInterfaceGuid;

        Logging.Log($"Found device: {_devicePath}");
        Logging.Log($"  Interface: {(_deviceGuid == ComPortGuid ? "Serial Port" : "libusb via WinUSB")}");
        Logging.Log($"  Bus Name: {finalBusName}", LogLevel.Debug);

        if (finalBusName?.StartsWith("QUSB_BULK", StringComparison.OrdinalIgnoreCase) == true ||
            finalBusName == "QHSUSB_DLOAD" ||
            finalBusName == "QHSUSB__BULK")
        {
            Logging.Log("  Mode detected: Sahara/Firehose");
        }
        else if (finalBusName == "QHSUSB_ARMPRG")
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

    public async Task<GptPartition?> FindPartitionAsync(string partitionName, uint? specifiedLun = null)
    {
        return await StorageBackend.FindPartitionAsync(partitionName, specifiedLun);
    }

    // Helper method to find partition and return both partition info and LUN
    public async Task<(GptPartition partition, uint lun)?> FindPartitionWithLunAsync(string partitionName, uint? specifiedLun = null)
    {
        return await StorageBackend.FindPartitionWithLunAsync(partitionName, specifiedLun);
    }

    private bool IsQualcommEdlDevice(string devicePath, string _)
    {
        var targetVid = globalOptions.Vid ?? DefaultVid;
        var targetPids = globalOptions.Pid.HasValue ? [globalOptions.Pid.Value] : DefaultPids;

        var isQualcomm = devicePath.Contains($"VID_{targetVid:X4}&", StringComparison.OrdinalIgnoreCase);
        var isEdl = targetPids.Any(pid => devicePath.Contains($"&PID_{pid:X4}", StringComparison.OrdinalIgnoreCase));

        return isQualcomm && isEdl;
    }

    /// <summary>
    /// Ensures the device is connected and in Firehose mode, uploading the loader if necessary.
    /// </summary>
    public async Task EnsureFirehoseModeAsync()
    {
        if (CurrentMode == DeviceMode.Firehose && _firehoseClient != null)
        {
            Logging.Log("Already in Firehose mode.", LogLevel.Debug);
            return;
        }

        var mode = await DetectCurrentModeAsync(forceReconnect: true);
        switch (mode)
        {
            case DeviceMode.Firehose:
                Logging.Log("Device is in Firehose mode. Establishing connection...", LogLevel.Debug);
                _transport?.Dispose();
                _transport = OpenTransport();
                _firehoseClient = new(_transport);
                _firehoseConfigured = false;
                CurrentMode = DeviceMode.Firehose;
                break;
            case DeviceMode.Sahara:
                Logging.Log("Device is in Sahara mode. Uploading loader...");
                await UploadLoaderViaSaharaAsync();
                Logging.Log("Waiting for device to re-enumerate in Firehose mode...", LogLevel.Debug);
                await Task.Delay(500);

                // Clear old path/state and find the device again
                _devicePath = null;
                _transport = null;
                _firehoseClient = null;
                _saharaClient = null;
                CurrentMode = DeviceMode.Unknown;
                if (!FindDevice()) // Find the potentially new device path
                {
                    throw new TodoException("Device did not re-enumerate in Firehose mode after loader upload, or could not be found.");
                }
                // Now establish the Firehose connection
                Logging.Log("Connecting to re-enumerated device in Firehose mode...", LogLevel.Debug);
                _transport = OpenTransport();
                _firehoseClient = new(_transport);

                FlushForResponse();

                CurrentMode = DeviceMode.Firehose;
                _firehoseConfigured = false;
                break;
            case DeviceMode.SaharaMemoryDebug:
                throw new InvalidOperationException("Device is in Sahara MemoryDebug (crashdump) mode. Firehose operations are not available. A different command is needed to handle this state.");
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
                        Logging.Log("DEVPRG LOG: " + data.Log.Value, LogLevel.Debug);
                    }
                    else if (data.Response != null)
                    {
                        gotResponse = true;
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
        if (string.IsNullOrEmpty(globalOptions.LoaderPath))
        {
            throw new ArgumentException("No loader (--loader) specified, and auto-detection not implemented yet.");
        }
        if (!File.Exists(globalOptions.LoaderPath))
        {
            throw new FileNotFoundException($"Loader file not found: {globalOptions.LoaderPath}");
        }

        if (_saharaClient == null || _transport == null)
        {
            Logging.Log("Sahara client not pre-established, creating new connection.", LogLevel.Debug);
            if (string.IsNullOrEmpty(_devicePath))
            {
                if (!FindDevice())
                {
                    throw new InvalidOperationException("Failed to find a suitable EDL device before Sahara upload.");
                }
            }

            _transport?.Dispose();
            _transport = OpenTransport();
            _saharaClient = new(_transport);
            _initialSaharaHelloPacket = null;
        }
        else
        {
            Logging.Log("Using pre-established Sahara connection.", LogLevel.Debug);
        }

        if (_deviceGuid == ComPortGuid && _initialSaharaHelloPacket == null) // Only if using COM port and not using pre-read packet
        {
            Logging.Log("Device is COM Port and no pre-read HELLO. Sending ResetStateMachine command to device...", LogLevel.Debug);
            try
            {
                var resetStateMachineCmd = QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.ResetStateMachine);
                _transport.SendData(resetStateMachineCmd);
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
                    if (!_saharaClient.CommandHandshake())
                    {
                        throw new TodoException("Sahara handshake failed even after reset.");
                    }
                    Logging.Log("Sahara handshake successful after reset.");
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
                Logging.Log($"Serial Number: {Convert.ToHexString(sn)}");
                if (deviceVersion < 3)
                {
                    Logging.Log("Sahara version < 3, attempting to get HWID and RKH.", LogLevel.Debug);
                    var hwid = _saharaClient.GetHwid();
                    HardwareId.ParseHwid(hwid);
                }
                else
                {
                    Logging.Log("Sahara version >= 3, skipping HWID retrieval.", LogLevel.Debug);
                }

                var rkhs = _saharaClient.GetRkHs();
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
            _saharaClient.SwitchMode(QualcommSaharaMode.ImageTxPending);
            await Task.Delay(100);

            if (globalOptions.LoaderPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                Logging.Log($"Parsing QSahara programmer XML: {globalOptions.LoaderPath}");
            }
            else
            {
                Logging.Log($"Uploading loader: {globalOptions.LoaderPath}");
            }

            var success = await _saharaClient.LoadProgrammer(globalOptions.LoaderPath);

            if (!success)
            {
                throw new TodoException("Failed to upload programmer via Sahara.");
            }
            Logging.Log("Loader uploaded and started successfully via Sahara.");
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
            _transport?.Dispose();
            _transport = null;
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
            Logging.Log("Sending Firehose configure command...");

            var storage = globalOptions.MemoryType ?? StorageType.Ufs;
            var maxPayload = globalOptions.MaxPayloadSize ?? 1048576;

            var success = await Task.Run(() => _firehoseClient.Configure(storage));

            if (!success)
            {
                // The Configure method in the provided QCEDL.NET doesn't seem to return false,
                // it relies on exceptions or log parsing. We might need to adjust it or add checks here.
                Logging.Log("Firehose configuration might have failed (check logs).", LogLevel.Warning);
            }
            Logging.Log($"Firehose configured for Memory: {storage}, MaxPayload: {maxPayload}\n");
            _firehoseConfigured = true;
        }
        catch (Exception ex)
        {
            Logging.Log($"Error during Firehose configuration: {ex.Message}", LogLevel.Error);
            Logging.Log(ex.ToString(), LogLevel.Debug);
            throw;
        }
    }

    public async Task<byte[]> ReadSectorsAsync(uint lun, ulong startSector, uint sectorCount)
    {
        return await StorageBackend.ReadSectorsAsync(lun, startSector, sectorCount);
    }

    public async Task WriteSectorsFromStreamAsync(uint lun, ulong startSector, Stream source, long sourceLength, bool padToSector, string sourceName, Action<long, long>? progressCallback = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException("Source stream must be readable.", nameof(source));
        }

        await StorageBackend.WriteSectorsAsync(lun, startSector, source, sourceLength, padToSector, sourceName, progressCallback);
    }

    public async Task WriteSectorsFromFileAsync(uint lun, ulong startSector, FileInfo inputFile, bool padToSector, string? sourceName = null, Action<long, long>? progressCallback = null)
    {
        if (!inputFile.Exists)
        {
            throw new FileNotFoundException($"Input file not found: {inputFile.FullName}", inputFile.FullName);
        }

        await using var stream = inputFile.OpenRead();
        await WriteSectorsFromStreamAsync(lun, startSector, stream, stream.Length, padToSector, sourceName ?? inputFile.Name, progressCallback);
    }

    public async Task EraseSectorsAsync(uint lun, ulong startSector, ulong sectorCount, Action<long, long>? progressCallback = null)
    {
        await StorageBackend.EraseSectorsAsync(lun, startSector, sectorCount, progressCallback);
    }

    public uint GetSectorSize(uint lun = 0)
    {
        return StorageBackend.GetDefaultSectorSize(lun);
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
                _transport?.Dispose();
                _hostDeviceManager?.Dispose();
                _radxaWoSManager?.Dispose();
            }

            _transport = null;
            _saharaClient = null;
            _firehoseClient = null;
            _hostDeviceManager = null;
            _radxaWoSManager = null;
            _devicePath = null;
            XstorageBackend = null;
            _firehoseConfigured = false;

            _disposed = true;
        }
    }

    internal interface IStorageBackend
    {
        Task<GptPartition?> FindPartitionAsync(string partitionName, uint? specifiedLun);
        Task<(GptPartition partition, uint lun)?> FindPartitionWithLunAsync(string partitionName, uint? specifiedLun);
        Task<byte[]> ReadSectorsAsync(uint lun, ulong startSector, uint sectorCount);
        Task ReadSectorsToStreamAsync(uint lun, ulong startSector, ulong sectorCount, Stream destination, Action<long, long>? progressCallback);
        Task<StorageGeometry> GetGeometryAsync(uint lun);
        uint GetDefaultSectorSize(uint lun);
        Task EraseSectorsAsync(uint lun, ulong startSector, ulong sectorCount, Action<long, long>? progressCallback);
        Task WriteSectorsAsync(uint lun, ulong startSector, Stream source, long sourceLength, bool padToSector, string sourceName, Action<long, long>? progressCallback);
        Task<List<uint>> DetermineLunsToScanAsync(uint? specifiedLun);
    }

    internal abstract class DirectStorageBackendBase(BlockDeviceManagerBase device) : IStorageBackend
    {
        private readonly BlockDeviceManagerBase _device = device;

        public Task<GptPartition?> FindPartitionAsync(string partitionName, uint? specifiedLun)
        {
            WarnIfUnsupportedLun(specifiedLun);
            return Task.Run(() => _device.FindPartition(partitionName));
        }

        public async Task<(GptPartition partition, uint lun)?> FindPartitionWithLunAsync(string partitionName, uint? specifiedLun)
        {
            var partition = await FindPartitionAsync(partitionName, specifiedLun);
            return partition.HasValue ? (partition.Value, 0u) : null;
        }

        public Task<byte[]> ReadSectorsAsync(uint lun, ulong startSector, uint sectorCount)
        {
            WarnIfUnsupportedLun(lun);
            return Task.Run(() => _device.ReadSectors(startSector, sectorCount));
        }

        public Task ReadSectorsToStreamAsync(uint lun, ulong startSector, ulong sectorCount, Stream destination, Action<long, long>? progressCallback)
        {
            WarnIfUnsupportedLun(lun);
            return Task.Run(() => _device.ReadSectorsToStream(startSector, sectorCount, destination, progressCallback));
        }

        public Task<StorageGeometry> GetGeometryAsync(uint lun)
        {
            WarnIfUnsupportedLun(lun);
            var totalSectors = _device.TotalSectors;
            var blocks = totalSectors == 0 ? (ulong?)null : totalSectors;
            return Task.FromResult(new StorageGeometry(_device.SectorSize, blocks));
        }

        public uint GetDefaultSectorSize(uint _)
        {
            return _device.SectorSize;
        }

        public Task EraseSectorsAsync(uint lun, ulong startSector, ulong sectorCount, Action<long, long>? progressCallback)
        {
            WarnIfUnsupportedLun(lun);
            return Task.Run(() => _device.EraseSectors(startSector, sectorCount, progressCallback));
        }

        public Task WriteSectorsAsync(uint lun, ulong startSector, Stream source, long sourceLength, bool padToSector, string sourceName, Action<long, long>? progressCallback)
        {
            WarnIfUnsupportedLun(lun);
            return Task.Run(() => _device.WriteStream(startSector, source, sourceLength, padToSector, progressCallback));
        }

        public Task<List<uint>> DetermineLunsToScanAsync(uint? specifiedLun)
        {
            // Direct backends only support LUN 0; default to 0 when no LUN is specified.
            WarnIfUnsupportedLun(specifiedLun);
            return Task.FromResult(new List<uint> { 0u });
        }

        private static void WarnIfUnsupportedLun(uint? lun)
        {
            if (lun.HasValue)
            {
                WarnIfUnsupportedLun(lun.Value);
            }
        }

        private static void WarnIfUnsupportedLun(uint lun)
        {
            if (lun != 0)
            {
                Logging.Log("Warning: LUN parameter is ignored in host device mode.", LogLevel.Warning);
            }
        }
    }

    private sealed class HostStorageBackend(HostDeviceManager hostManager) : DirectStorageBackendBase(hostManager);

    private sealed class RadxaWoSBackend(RadxaWoSDeviceManager manager) : DirectStorageBackendBase(manager);

    internal sealed class FirehoseStorageBackend(EdlManager owner) : IStorageBackend
    {
        private readonly Dictionary<uint, StorageGeometry> _geometryCache = [];

        public async Task<GptPartition?> FindPartitionAsync(string partitionName, uint? specifiedLun)
        {
            var result = await FindPartitionWithLunAsync(partitionName, specifiedLun);
            return result?.partition;
        }

        public async Task<(GptPartition partition, uint lun)?> FindPartitionWithLunAsync(string partitionName, uint? specifiedLun)
        {
            var lunsToScan = await DetermineLunsToScanAsync(specifiedLun);

            foreach (var currentLun in lunsToScan)
            {
                var partition = await FindPartitionOnLunAsync(partitionName, currentLun);
                if (partition.HasValue)
                {
                    return (partition.Value, currentLun);
                }
            }

            return null;
        }

        public async Task<byte[]> ReadSectorsAsync(uint lun, ulong startSector, uint sectorCount)
        {
            ValidateLbaRange(startSector, sectorCount);
            var geometry = await GetGeometryAsync(lun);

            await EnsureReadyAsync();

            var firstLba = (uint)startSector;
            var lastLba = (uint)(startSector + sectorCount - 1);

            var data = await Task.Run(() => owner.Firehose.Read(
                CurrentStorageType,
                lun,
                owner.GlobalOptions.Slot,
                geometry.SectorSize,
                firstLba,
                lastLba
            ));

            return data ?? throw new InvalidOperationException("Failed to read data from device");
        }

        public async Task ReadSectorsToStreamAsync(uint lun, ulong startSector, ulong sectorCount, Stream destination, Action<long, long>? progressCallback)
        {
            ArgumentNullException.ThrowIfNull(destination);

            if (sectorCount == 0)
            {
                return;
            }

            ValidateLbaRange(startSector, sectorCount);
            var geometry = await GetGeometryAsync(lun);

            await EnsureReadyAsync();

            var firstLba = (uint)startSector;
            var lastLba = (uint)(startSector + sectorCount - 1);

            var success = await Task.Run(() => owner.Firehose.ReadToStream(
                CurrentStorageType,
                lun,
                owner.GlobalOptions.Slot,
                geometry.SectorSize,
                firstLba,
                lastLba,
                destination,
                progressCallback
            ));

            if (!success)
            {
                throw new InvalidOperationException("Failed to read sector data or write to stream.");
            }
        }

        public async Task<StorageGeometry> GetGeometryAsync(uint lun)
        {
            if (_geometryCache.TryGetValue(lun, out var cached))
            {
                return cached;
            }

            var storageInfo = await GetDeviceInfoAsync(lun);
            var sectorSize = DetermineSectorSize(storageInfo, logFallback: true);
            ulong? totalBlocks = storageInfo?.StorageInfo?.TotalBlocks > 0 ? (ulong)storageInfo.StorageInfo.TotalBlocks : null;

            var geometry = new StorageGeometry(sectorSize, totalBlocks);
            _geometryCache[lun] = geometry;
            return geometry;
        }

        public uint GetDefaultSectorSize(uint lun)
        {
            return _geometryCache.TryGetValue(lun, out var cached)
                ? cached.SectorSize
                : DetermineSectorSize(null, logFallback: false);
        }

        public async Task EraseSectorsAsync(uint lun, ulong startSector, ulong sectorCount, Action<long, long>? progressCallback)
        {
            ValidateLbaRange(startSector, sectorCount);
            await EnsureReadyAsync();

            var geometry = await GetGeometryAsync(lun);
            var start = (uint)startSector;
            var count = (uint)sectorCount;

            var success = await Task.Run(() => owner.Firehose.Erase(
                CurrentStorageType,
                lun,
                owner.GlobalOptions.Slot,
                geometry.SectorSize,
                start,
                count
            ));

            if (!success)
            {
                throw new InvalidOperationException("Failed to erase sectors.");
            }

            var totalBytes = CalculateTotalBytes(sectorCount, geometry.SectorSize);
            progressCallback?.Invoke(totalBytes, totalBytes);
        }

        public async Task WriteSectorsAsync(uint lun, ulong startSector, Stream source, long sourceLength, bool padToSector, string sourceName, Action<long, long>? progressCallback)
        {
            ArgumentNullException.ThrowIfNull(source);

            if (sourceLength < 0)
            {
                throw new ArgumentException("Source length cannot be negative.", nameof(sourceLength));
            }

            var geometry = await GetGeometryAsync(lun);
            var totalBytes = padToSector ? AlignmentHelper.AlignTo((ulong)sourceLength, geometry.SectorSize) : (ulong)sourceLength;
            if (totalBytes == 0)
            {
                Logging.Log("Write request contains zero bytes. Skipping.", LogLevel.Debug);
                return;
            }

            var sectorCount = totalBytes / geometry.SectorSize;
            ValidateLbaRange(startSector, sectorCount);

            if (startSector > uint.MaxValue)
            {
                throw new ArgumentException("Start sector exceeds Firehose limit (uint32).", nameof(startSector));
            }

            if (sectorCount > uint.MaxValue)
            {
                throw new ArgumentException("Write length exceeds Firehose limit (uint32 sectors).", nameof(sourceLength));
            }

            if (totalBytes > long.MaxValue)
            {
                throw new ArgumentException("Write size exceeds supported range.", nameof(sourceLength));
            }

            await EnsureReadyAsync();

            var success = await Task.Run(() => owner.Firehose.ProgramFromStream(
                CurrentStorageType,
                lun,
                owner.GlobalOptions.Slot,
                geometry.SectorSize,
                (uint)startSector,
                (uint)sectorCount,
                (long)totalBytes,
                sourceName,
                source,
                progressCallback
            ));

            if (!success)
            {
                throw new InvalidOperationException("Failed to write data to device.");
            }
        }

        private async Task<Root?> GetDeviceInfoAsync(uint lun)
        {
            await EnsureReadyAsync();
            try
            {
                return await Task.Run(() => owner.Firehose.GetStorageInfo(CurrentStorageType, lun, owner.GlobalOptions.Slot));
            }
            catch (Exception ex)
            {
                Logging.Log($"Could not get storage info for LUN {lun} (StorageType: {CurrentStorageType}). Using defaults. Error: {ex.Message}", LogLevel.Warning);
                return null;
            }
        }

        private async Task<GptPartition?> FindPartitionOnLunAsync(string partitionName, uint lun)
        {
            Logging.Log($"Scanning LUN {lun} for partition '{partitionName}'...", LogLevel.Debug);

            try
            {
                var geometry = await GetGeometryAsync(lun);
                var gptData = await ReadSectorsAsync(lun, 0, 64);
                using var stream = new MemoryStream(gptData);

                var gpt = Gpt.ReadFromStream(stream, (int)geometry.SectorSize);
                if (gpt == null)
                {
                    return null;
                }

                foreach (var partition in gpt.Partitions)
                {
                    var currentPartitionName = partition.GetName().TrimEnd('\0');
                    if (currentPartitionName.Equals(partitionName, StringComparison.OrdinalIgnoreCase))
                    {
                        Logging.Log($"Found partition '{partitionName}' on LUN {lun}");
                        return partition;
                    }
                }
            }
            catch (InvalidDataException)
            {
                Logging.Log($"No valid GPT found on LUN {lun}.", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                Logging.Log($"Error scanning LUN {lun}: {ex.Message}", LogLevel.Warning);
            }

            return null;
        }

        public async Task<List<uint>> DetermineLunsToScanAsync(uint? specifiedLun)
        {
            if (specifiedLun.HasValue)
            {
                Logging.Log($"Scanning specified LUN: {specifiedLun.Value}", LogLevel.Debug);
                return [specifiedLun.Value];
            }

            Logging.Log("No LUN specified, attempting to determine number of LUNs and scan all.", LogLevel.Debug);

            var devInfo = await GetDeviceInfoAsync(0);
            if (devInfo?.StorageInfo?.NumPhysical > 0)
            {
                var luns = new List<uint>();
                for (uint i = 0; i < devInfo.StorageInfo.NumPhysical; i++)
                {
                    luns.Add(i);
                }
                Logging.Log($"Device reports {devInfo.StorageInfo.NumPhysical} LUN(s). Scanning LUNs: {string.Join(", ", luns)}", LogLevel.Debug);
                return luns;
            }

            if (CurrentStorageType == StorageType.Spinor)
            {
                return [0];
            }

            List<uint> fallback = [0, 1, 2, 3, 4, 5];
            Logging.Log($"Could not determine LUN count. Scanning default LUNs: {string.Join(", ", fallback)}", LogLevel.Warning);
            return fallback;
        }

        private async Task EnsureReadyAsync()
        {
            await owner.EnsureFirehoseModeAsync();
            if (!owner._firehoseConfigured)
            {
                await owner.ConfigureFirehoseAsync();
            }
        }

        private StorageType CurrentStorageType => owner.GlobalOptions.MemoryType ?? StorageType.Ufs;

        private uint DetermineSectorSize(Root? storageInfo, bool logFallback)
        {
            if (storageInfo?.StorageInfo?.BlockSize > 0)
            {
                return (uint)storageInfo.StorageInfo.BlockSize;
            }

            var sectorSize = DefaultSectorSize;
            if (logFallback)
            {
                Logging.Log($"Storage info unreliable or unavailable, using default sector size for {CurrentStorageType}: {sectorSize}", LogLevel.Warning);
            }

            return sectorSize;
        }

        private uint DefaultSectorSize => CurrentStorageType switch
        {
            StorageType.Nvme => 512,
            StorageType.Sdcc => 512,
            StorageType.Spinor or StorageType.Ufs or StorageType.Nand or _ => 4096,
        };

        private static void ValidateLbaRange(ulong startSector, ulong sectorCount)
        {
            if (sectorCount == 0)
            {
                throw new ArgumentException("Sector count must be greater than zero", nameof(sectorCount));
            }

            var lastSector = startSector + sectorCount - 1;
            if (startSector > uint.MaxValue || lastSector > uint.MaxValue)
            {
                throw new ArgumentException("Sector range exceeds uint.MaxValue, which is not supported by the current Firehose implementation.");
            }
        }

        private static long CalculateTotalBytes(ulong sectorCount, uint sectorSize)
        {
            try
            {
                var bytes = checked(sectorCount * sectorSize);
                return bytes > long.MaxValue ? long.MaxValue : (long)bytes;
            }
            catch (OverflowException)
            {
                return long.MaxValue;
            }
        }

    }
}