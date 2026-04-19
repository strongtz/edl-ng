using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using LibUsbDotNet.LibUsb;
using LibUsbDotNet.Main;
using QCEDL.NET.PartitionTable;
using QCEDL.NET.Todo;
using QCEDL.NET.USB;
using Qualcomm.EmergencyDownload.ChipInfo;
using Qualcomm.EmergencyDownload.Helpers;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.JSON.StorageInfo;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;
using Qualcomm.EmergencyDownload.Layers.PBL.Sahara;
using Qualcomm.EmergencyDownload.Transport;

namespace Qualcomm.EmergencyDownload.Core;

public sealed class EdlManager(EdlOptions globalOptions) : IDisposable
{
    private string? _devicePath;
    private Guid? _deviceGuid;
    private QualcommSerial? _serialPort;
    private QualcommSahara? _saharaClient;
    private QualcommFirehose? _firehoseClient;
    internal IStorageBackend? XstorageBackend;

    /// <summary>The options this manager was initialised with.</summary>
    public EdlOptions Options => globalOptions;
    private bool _firehoseConfigured;
    private bool _disposed;

    // GUIDs for device detection on Windows
    private static readonly Guid ComPortGuid = new("{86E0D1E0-8089-11D0-9CE4-08003E301F73}");
    private static readonly Guid WinUsbGuid = new("{a5dcbf10-6530-11d2-901f-00c04fb951ed}");

    // Default USB device IDs
    private static readonly int DefaultVid = 0x05C6;
    private static readonly int[] DefaultPids = [0x9008, 0x900E];

    private byte[]? _initialSaharaHelloPacket;

    // USB iSerial captured on the initial pinned open. Used across Sahara-to-Firehose
    // re-enumeration (where bus/addr typically change) so we re-latch onto the same
    // logical device instead of silently first-matching a different EDL device.
    private string? _pinnedUsbSerial;
    private bool _initialPinApplied;

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

    private EdlOptions GlobalOptions => globalOptions;

    private string ResolveTransportPath()
    {
        return _devicePath ?? throw new InvalidOperationException("Device path not resolved yet.");
    }

    private void CapturePinnedUsbSerial()
    {
        if (_pinnedUsbSerial != null)
        {
            return;
        }
        var sn = _serialPort?.UsbSerialNumber;
        if (!string.IsNullOrWhiteSpace(sn))
        {
            _pinnedUsbSerial = sn;
            Logging.Log($"LibUsbDotNet: captured iSerial='{sn}' from open handle for cross-reenum pinning.", LogLevel.Debug);
        }
    }

    private void EnsureValidDirectMode()
    {
        if (IsHostDeviceMode && IsRadxaWosMode)
        {
            throw new InvalidOperationException("Cannot use --hostdev-as-target and --radxa-wos-platform at the same time.");
        }
    }

    internal HostDeviceManager GetHostDeviceManager()
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

    /// <summary>
    /// Returns the LUN set to scan given an optional caller-specified LUN. For direct-mode
    /// backends this is always <c>[0]</c>; for Firehose it queries the device.
    /// </summary>
    public Task<List<uint>> DetermineLunsToScanAsync(uint? specifiedLun)
    {
        return StorageBackend.DetermineLunsToScanAsync(specifiedLun);
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
        if (CurrentMode != DeviceMode.Unknown && _serialPort != null && !forceReconnect)
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
            _serialPort?.Dispose();
            _serialPort = null;
            _saharaClient = null;
            _firehoseClient = null;
            CurrentMode = DeviceMode.Unknown;
            _initialSaharaHelloPacket = null;
            _firehoseConfigured = false;
        }

        Logging.Log("Probing device mode...", LogLevel.Debug);
        QualcommSerial? probeSerial = null;
        var detectedMode = DeviceMode.Unknown;
        byte[]? initialReadBuffer = null;

        try
        {
            probeSerial = new(ResolveTransportPath());
            if (_pinnedUsbSerial == null && !string.IsNullOrWhiteSpace(probeSerial.UsbSerialNumber))
            {
                _pinnedUsbSerial = probeSerial.UsbSerialNumber;
                Logging.Log($"LibUsbDotNet: captured iSerial='{_pinnedUsbSerial}' from probe handle.", LogLevel.Debug);
            }
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

                    _serialPort = probeSerial; // Keep the probeSerial as the main _serialPort
                    probeSerial = null; // Nullify probeSerial so it's not disposed in finally if kept
                    _saharaClient = new(_serialPort);
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
                        var nopCommand = QualcommFirehoseXml.BuildCommandPacket([new() { Nop = new() }]);
                        firehoseProbe.Serial.SendData(Encoding.UTF8.GetBytes(nopCommand));
                        var datas = await Task.Run(() => firehoseProbe.GetFirehoseResponseDataPayloads());
                        if (datas.Length > 0)
                        {
                            Logging.Log("Firehose NOP probe successful. Detected Mode: Firehose", LogLevel.Debug);
                            detectedMode = DeviceMode.Firehose;

                            Logging.Log("Flushing serial output...", LogLevel.Debug);
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
                var serialForSaharaProbe = _serialPort ?? probeSerial;
                if (serialForSaharaProbe == null)
                {
                    Logging.Log("No serial port available for Sahara handshake probe.", LogLevel.Error);
                }
                else
                {
                    Logging.Log("Probes inconclusive. Attempting *full* Sahara handshake as last resort...");
                    try
                    {
                        serialForSaharaProbe.SetTimeOut(2000);
                        var saharaProbeClient = new QualcommSahara(serialForSaharaProbe);
                        // Pass null to CommandHandshake as we don't have a pre-read packet here
                        if (await Task.Run(() => saharaProbeClient.CommandHandshake()))
                        {
                            Logging.Log("Full Sahara handshake probe successful. Detected Mode: Sahara");
                            detectedMode = DeviceMode.Sahara;
                            // If successful, this becomes the main connection
                            _serialPort = serialForSaharaProbe;
                            probeSerial = null; // Don't dispose it
                            _saharaClient = saharaProbeClient;
                            CurrentMode = DeviceMode.Sahara;
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

        if (CurrentMode == DeviceMode.Unknown)
        {
            CurrentMode = detectedMode;
        }
        Logging.Log($"Detected device mode: {CurrentMode}");
        return CurrentMode;
    }

    /// <summary>
    /// Enumerates EDL-capable devices visible to the selected transport backend.
    /// Non-destructive: does not open any device, so it is safe to call while a
    /// session is active. Honors <see cref="EdlOptions.Vid"/> / <see cref="EdlOptions.Pid"/>
    /// overrides and the chosen <see cref="EdlOptions.Backend"/>. Direct-mode options
    /// (<c>--hostdev-as-target</c>, <c>--radxa-wos-platform</c>) bypass device discovery
    /// entirely and return an empty list.
    /// </summary>
    public static IReadOnlyList<DeviceCandidate> EnumerateDevices(EdlOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        var isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        if (!isWindows && !isLinux && !isMac)
        {
            return [];
        }

        var backend = ResolveBackend(options.Backend, isWindows, isMac);
        return backend == TransportBackend.Serial
            ? isLinux ? EnumerateLinuxSerial(options)
                : isWindows ? EnumerateWindowsSerial(options)
                : isMac ? EnumerateMacOsSerial()
                : []
            : EnumerateLibUsb(options);
    }

    private static List<DeviceCandidate> EnumerateLibUsb(EdlOptions options)
    {
        if (QualcommSerial.LibUsbContext == null)
        {
            return [];
        }

        var vid = options.Vid ?? DefaultVid;
        var pids = options.Pid.HasValue ? [options.Pid.Value] : DefaultPids;

        var results = new List<DeviceCandidate>();
        try
        {
            foreach (var candidate in QualcommSerial.LibUsbContext.List())
            {
                if (candidate is not UsbDevice dev || dev.VendorId != vid)
                {
                    continue;
                }
                if (!pids.Contains(dev.ProductId))
                {
                    continue;
                }

                var id = FormatUsbDevicePath(dev.VendorId, dev.ProductId, dev.BusNumber, dev.Address);
                var display = $"USB {dev.VendorId:X4}:{dev.ProductId:X4} @ bus {dev.BusNumber} addr {dev.Address}";
                var details = $"VID=0x{dev.VendorId:X4}, PID=0x{dev.ProductId:X4}, bus={dev.BusNumber}, addr={dev.Address}";
                results.Add(new DeviceCandidate(id, display, details, TransportBackend.LibUsb));
            }
        }
        catch (Exception ex)
        {
            Logging.Log($"LibUsb enumeration failed: {ex.Message}", LogLevel.Warning);
        }
        return results;
    }

    private static List<DeviceCandidate> EnumerateLinuxSerial(EdlOptions options)
    {
        var targetVid = (options.Vid ?? DefaultVid).ToString("x4", CultureInfo.InvariantCulture);
        var targetPids = options.Pid.HasValue
            ? [options.Pid.Value.ToString("x4", CultureInfo.InvariantCulture)]
            : DefaultPids.Select(p => p.ToString("x4", CultureInfo.InvariantCulture)).ToArray();

        var results = new List<DeviceCandidate>();
        try
        {
            var sysTtyPath = "/sys/class/tty";
            if (!Directory.Exists(sysTtyPath))
            {
                return results;
            }
            foreach (var dirName in Directory.GetDirectories(sysTtyPath))
            {
                var ttyName = Path.GetFileName(dirName);
                if (!ttyName.StartsWith("ttyUSB", StringComparison.Ordinal) &&
                    !ttyName.StartsWith("ttyACM", StringComparison.Ordinal))
                {
                    continue;
                }

                var realDevicePath = new FileInfo(dirName).ResolveLinkTarget(true)?.FullName;
                if (realDevicePath == null)
                {
                    continue;
                }
                var ttyDir = Directory.GetParent(realDevicePath);
                var usbInterfaceDir = ttyDir?.Parent;
                var usbDeviceDir = usbInterfaceDir?.Parent;
                if (usbDeviceDir == null)
                {
                    continue;
                }
                if (ttyName.StartsWith("ttyUSB", StringComparison.Ordinal))
                {
                    usbDeviceDir = usbDeviceDir.Parent;
                    if (usbDeviceDir == null)
                    {
                        continue;
                    }
                }

                var vidPath = Path.Combine(usbDeviceDir.FullName, "idVendor");
                var pidPath = Path.Combine(usbDeviceDir.FullName, "idProduct");
                if (!File.Exists(vidPath) || !File.Exists(pidPath))
                {
                    continue;
                }
                var vid = File.ReadAllText(vidPath).Trim();
                var pid = File.ReadAllText(pidPath).Trim();
                if (!vid.Equals(targetVid, StringComparison.OrdinalIgnoreCase) ||
                    !targetPids.Any(tp => pid.Equals(tp, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                var devPath = Path.Combine("/dev", ttyName);
                if (!File.Exists(devPath))
                {
                    continue;
                }
                results.Add(new DeviceCandidate(
                    devPath,
                    devPath,
                    $"VID={vid}, PID={pid}",
                    TransportBackend.Serial));
            }
        }
        catch (Exception ex)
        {
            Logging.Log($"Linux serial enumeration failed: {ex.Message}", LogLevel.Warning);
        }
        return results;
    }

    private static List<DeviceCandidate> EnumerateMacOsSerial()
    {
        var results = new List<DeviceCandidate>();
        try
        {
            foreach (var path in Directory.EnumerateFiles("/dev", "tty.usbserial-*", SearchOption.TopDirectoryOnly)
                         .OrderBy(p => p, StringComparer.Ordinal))
            {
                results.Add(new DeviceCandidate(path, path, "/dev/tty.usbserial-*", TransportBackend.Serial));
            }
        }
        catch (Exception ex)
        {
            Logging.Log($"macOS serial enumeration failed: {ex.Message}", LogLevel.Warning);
        }
        return results;
    }

    private static List<DeviceCandidate> EnumerateWindowsSerial(EdlOptions options)
    {
        var targetVid = options.Vid ?? DefaultVid;
        var targetPids = options.Pid.HasValue ? [options.Pid.Value] : DefaultPids;

        var results = new List<DeviceCandidate>();
        try
        {
            var seen = new HashSet<int>();
            foreach (var (pathName, busName, devInst) in UsbExtensions.GetDeviceInfos(ComPortGuid))
            {
                if (pathName is null)
                {
                    continue;
                }
                var isQualcomm = pathName.Contains($"VID_{targetVid:X4}&", StringComparison.OrdinalIgnoreCase);
                var isEdl = targetPids.Any(pid => pathName.Contains($"&PID_{pid:X4}", StringComparison.OrdinalIgnoreCase));
                if (!isQualcomm || !isEdl)
                {
                    continue;
                }
                if (!seen.Add(devInst))
                {
                    continue;
                }
                results.Add(new DeviceCandidate(
                    pathName,
                    string.IsNullOrEmpty(busName) ? pathName : busName,
                    $"Path: {pathName}",
                    TransportBackend.Serial));
            }
        }
        catch (Exception ex)
        {
            Logging.Log($"Windows serial enumeration failed: {ex.Message}", LogLevel.Warning);
        }
        return results;
    }

    /// <summary>
    /// Finds a compatible EDL device.
    /// </summary>
    /// <returns>True if a device is found, false otherwise.</returns>
    /// <exception cref="Exception">Throws if multiple devices are found without specific selection criteria.</exception>
    private bool FindDevice()
    {
        Logging.Log("Searching for Qualcomm EDL device...", LogLevel.Trace);

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        var isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        if (!isWindows && !isLinux && !isMac)
        {
            Logging.Log($"Unsupported OS: {RuntimeInformation.OSDescription}. Device discovery skipped.", LogLevel.Error);
            return false;
        }

        var backend = ResolveBackend(globalOptions.Backend, isWindows, isMac);
        Logging.Log($"Transport backend: {backend} (requested: {globalOptions.Backend})", LogLevel.Debug);

        if (backend == TransportBackend.Serial)
        {
            if (!string.IsNullOrWhiteSpace(globalOptions.SerialDevicePath))
            {
                _devicePath = globalOptions.SerialDevicePath;
                _deviceGuid = ComPortGuid;
                Logging.Log($"Using explicit serial device path: {_devicePath}");
                return true;
            }

            if (isLinux)
            {
                return FindDeviceLinuxSerial();
            }

            if (isWindows)
            {
                return FindDeviceWindowsSerial();
            }

            if (isMac)
            {
                return FindDeviceMacOSSerial();
            }

            Logging.Log("Serial backend on this OS requires an explicit device path.", LogLevel.Error);
            return false;
        }

        // LibUsb backend (cross-platform via LibUsbDotNet).
        if (FindDeviceViaLibUsb())
        {
            Logging.Log("Found device using LibUsbDotNet.");
            return true;
        }

        Logging.Log("LibUsb backend selected but no device found.", LogLevel.Error);
        return false;
    }

    private static TransportBackend ResolveBackend(TransportBackend requested, bool isWindows, bool isMac)
    {
        return requested != TransportBackend.Auto
            ? requested
            : isWindows || isMac ? TransportBackend.LibUsb : TransportBackend.Serial;
    }

    private bool FindDeviceViaLibUsb()
    {
        Logging.Log("Trying to find device using LibUsbDotNet...", LogLevel.Debug);
        if (QualcommSerial.LibUsbContext == null)
        {
            Logging.Log("LibUsbDotNet context not initialized. Cannot use LibUsb backend.", LogLevel.Warning);
            return false;
        }

        var vidToFind = globalOptions.Vid ?? DefaultVid;
        var pidsToFind = globalOptions.Pid.HasValue ? [globalOptions.Pid.Value] : DefaultPids;

        try
        {
            var pinningInEffect = _initialPinApplied || !string.IsNullOrWhiteSpace(globalOptions.UsbDeviceId);

            // Honor a pinned selection first: parse bus/addr from UsbDeviceId and match that specific device.
            if (!_initialPinApplied &&
                !string.IsNullOrWhiteSpace(globalOptions.UsbDeviceId) &&
                TryParseUsbDeviceId(globalOptions.UsbDeviceId, out var pinnedVid, out var pinnedPid, out var pinnedBus, out var pinnedAddr))
            {
                foreach (var candidate in QualcommSerial.LibUsbContext.List())
                {
                    if (candidate is UsbDevice dev &&
                        dev.VendorId == pinnedVid &&
                        dev.ProductId == pinnedPid &&
                        dev.BusNumber == pinnedBus &&
                        dev.Address == pinnedAddr)
                    {
                        _devicePath = FormatUsbDevicePath(pinnedVid, pinnedPid, pinnedBus, pinnedAddr);
                        _deviceGuid = WinUsbGuid;
                        _initialPinApplied = true;
                        Logging.Log(
                            $"LibUsbDotNet: using pinned device {_devicePath} (iSerial will be captured after open)",
                            LogLevel.Debug);
                        return true;
                    }
                }
                Logging.Log($"LibUsbDotNet: pinned device '{globalOptions.UsbDeviceId}' not found.", LogLevel.Warning);
            }

            // Re-enumeration path: prefer the previously captured iSerial since USB bus/addr
            // typically change after the device re-enumerates (e.g. Sahara -> Firehose).
            if (_pinnedUsbSerial != null)
            {
                UsbDevice? bySerialMatch = null;
                var candidatesSeen = 0;
                foreach (var candidate in QualcommSerial.LibUsbContext.List())
                {
                    if (candidate is not UsbDevice dev || dev.VendorId != vidToFind || !pidsToFind.Contains(dev.ProductId))
                    {
                        continue;
                    }
                    candidatesSeen++;
                    var sn = TryReadUsbSerial(dev);
                    if (sn != null && string.Equals(sn, _pinnedUsbSerial, StringComparison.Ordinal))
                    {
                        bySerialMatch = dev;
                        break;
                    }
                }

                if (bySerialMatch != null)
                {
                    _devicePath = FormatUsbDevicePath(vidToFind, bySerialMatch.ProductId, bySerialMatch.BusNumber, bySerialMatch.Address);
                    _deviceGuid = WinUsbGuid;
                    Logging.Log($"LibUsbDotNet: re-matched pinned device by iSerial '{_pinnedUsbSerial}' at {_devicePath}", LogLevel.Debug);
                    return true;
                }

                if (candidatesSeen > 1)
                {
                    // Multiple EDL devices are visible and none matches our captured iSerial.
                    // Refuse to guess — silently grabbing the wrong one can brick a neighbour device.
                    Logging.Log(
                        $"LibUsbDotNet: pinned device (iSerial='{_pinnedUsbSerial}') not found after re-enumeration, and {candidatesSeen} other EDL devices are present. Refusing to fall back to first-match.",
                        LogLevel.Error);
                    return false;
                }
                // candidatesSeen <= 1: fall through to first-match (safe single-device case).
            }
            else if (pinningInEffect)
            {
                // Initial pin was requested but we never captured an iSerial (read failed or descriptor missing).
                // If >1 device is present now, refuse to guess.
                var candidatesSeen = 0;
                foreach (var candidate in QualcommSerial.LibUsbContext.List())
                {
                    if (candidate is UsbDevice dev && dev.VendorId == vidToFind && pidsToFind.Contains(dev.ProductId))
                    {
                        candidatesSeen++;
                    }
                }
                if (candidatesSeen > 1)
                {
                    Logging.Log(
                        $"LibUsbDotNet: pinning was requested ('{globalOptions.UsbDeviceId}') but iSerial is unavailable and {candidatesSeen} EDL devices are present. Refusing to fall back to first-match.",
                        LogLevel.Error);
                    return false;
                }
            }

            foreach (var pidToFind in pidsToFind)
            {
                var finder = new UsbDeviceFinder
                {
                    Vid = vidToFind,
                    Pid = pidToFind
                };

                if (QualcommSerial.LibUsbContext.Find(finder) is UsbDevice usbDevice)
                {
                    // Match main's historical path format (no bus/addr) when the caller hasn't
                    // explicitly pinned. `QualcommSerial.FindLibUsbDevice` takes the `Find(finder)`
                    // fast path when bus/addr are absent from the path; the `List()`-enumeration
                    // fallback has different endpoint/timeout behavior on macOS IOKit that causes
                    // the post-loader Sahara read to receive the Firehose hello instead of timing out.
                    // Bus/addr are only needed when disambiguating multiple EDL devices — logged
                    // for diagnostics either way.
                    _devicePath = $"usb:vid_{vidToFind:X4},pid_{pidToFind:X4}";
                    _deviceGuid = WinUsbGuid; // Use WinUSBGuid to signify LibUsbDotNet backend to QualcommSerial
                    Logging.Log($"LibUsbDotNet found device: VID={vidToFind:X4}, PID={pidToFind:X4} @ bus {usbDevice.BusNumber} addr {usbDevice.Address}. Path set to: {_devicePath}", LogLevel.Debug);
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

    private static string FormatUsbDevicePath(int vid, int pid, byte bus, byte addr)
    {
        return $"usb:vid_{vid:X4},pid_{pid:X4},bus_{bus},addr_{addr}";
    }

    /// <summary>
    /// Best-effort read of a libusb device's iSerial string. Opens the device transiently,
    /// reads <see cref="UsbDevice.Info"/>.<c>SerialNumber</c>, and closes it. Returns <c>null</c>
    /// if the device cannot be opened, has no iSerial descriptor, or any exception occurs —
    /// callers must treat iSerial as optional and degrade gracefully.
    /// </summary>
    private static string? TryReadUsbSerial(UsbDevice dev)
    {
        try
        {
            if (!dev.TryOpen())
            {
                return null;
            }
            try
            {
                var sn = dev.Info?.SerialNumber;
                return string.IsNullOrWhiteSpace(sn) ? null : sn;
            }
            finally
            {
                try { dev.Close(); } catch (Exception ex) { Logging.Log($"LibUsbDotNet: Close after iSerial read failed: {ex.Message}", LogLevel.Trace); }
            }
        }
        catch (Exception ex)
        {
            Logging.Log($"LibUsbDotNet: iSerial read failed: {ex.Message}", LogLevel.Trace);
            return null;
        }
    }

    private static bool TryParseUsbDeviceId(string id, out int vid, out int pid, out byte bus, out byte addr)
    {
        vid = pid = 0;
        bus = addr = 0;
        try
        {
            var vidMatch = System.Text.RegularExpressions.Regex.Match(id, @"vid_([0-9A-Fa-f]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var pidMatch = System.Text.RegularExpressions.Regex.Match(id, @"pid_([0-9A-Fa-f]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var busMatch = System.Text.RegularExpressions.Regex.Match(id, @"bus_([0-9]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var addrMatch = System.Text.RegularExpressions.Regex.Match(id, @"addr_([0-9]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!vidMatch.Success || !pidMatch.Success || !busMatch.Success || !addrMatch.Success)
            {
                return false;
            }
            vid = int.Parse(vidMatch.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            pid = int.Parse(pidMatch.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            bus = byte.Parse(busMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            addr = byte.Parse(addrMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool FindDeviceLinuxSerial()
    {
        Logging.Log("Searching for Qualcomm EDL device on Linux (/dev/ttyUSB* or /dev/ttyACM*)...");
        var targetVid = (globalOptions.Vid ?? DefaultVid).ToString("x4", CultureInfo.InvariantCulture);
        var targetPids = globalOptions.Pid.HasValue
            ? [globalOptions.Pid.Value.ToString("x4", CultureInfo.InvariantCulture)]
            : DefaultPids.Select(p => p.ToString("x4", CultureInfo.InvariantCulture)).ToArray();

        Logging.Log($"Target VID: {targetVid}, PIDs: {string.Join(", ", targetPids)}", LogLevel.Debug);
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
                if (ttyName.StartsWith("ttyUSB", StringComparison.Ordinal) || ttyName.StartsWith("ttyACM", StringComparison.Ordinal))
                {
                    Logging.Log($"TTY dirName: {dirName}", LogLevel.Trace);
                    var realDevicePath = new FileInfo(dirName).ResolveLinkTarget(true)?.FullName;
                    if (realDevicePath == null)
                    {
                        continue;
                    }

                    Logging.Log($"Found TTY: {ttyName}, Real Device Path: {realDevicePath}", LogLevel.Trace);

                    var ttyDir = Directory.GetParent(realDevicePath);
                    if (ttyDir == null)
                    {
                        continue;
                    }

                    var usbInterfaceDir = ttyDir.Parent;
                    if (usbInterfaceDir == null)
                    {
                        continue;
                    }

                    var usbDeviceDir = usbInterfaceDir.Parent;
                    if (usbDeviceDir == null)
                    {
                        continue;
                    }

                    if (ttyName.StartsWith("ttyUSB", StringComparison.Ordinal))
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
                        Logging.Log($"Found TTY: {ttyName}, VID: {vid}, PID: {pid}", LogLevel.Trace);

                        if (vid.Equals(targetVid, StringComparison.OrdinalIgnoreCase) &&
                            targetPids.Any(targetPid => pid.Equals(targetPid, StringComparison.OrdinalIgnoreCase)))
                        {
                            var devPath = Path.Combine("/dev", ttyName);
                            if (File.Exists(devPath)) // Check if /dev/ttyUSBx actually exists
                            {
                                potentialTtyPaths.Add(devPath);
                                Logging.Log($"Match: {devPath} for VID/PID {targetVid}/{pid}", LogLevel.Debug);
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
        _deviceGuid = ComPortGuid; // For some relevant logic
        Logging.Log($"Selected device: {_devicePath}");
        Logging.Log("  Mode detected: Serial Port (ttyUSB/ttyACM)");
        return true;
    }

    private bool FindDeviceMacOSSerial()
    {
        Logging.Log("Searching for Qualcomm EDL device on macOS (/dev/tty.usbserial-*)...");

        // macOS has no sysfs-equivalent exposing idVendor/idProduct per tty, so we
        // enumerate by filename only. If multiple candidates exist, callers should
        // pass --serial-device explicitly.
        List<string> candidates;
        try
        {
            candidates = [.. Directory
                .EnumerateFiles("/dev", "tty.usbserial-*", SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.Ordinal)];
        }
        catch (Exception ex)
        {
            Logging.Log($"Error during macOS device discovery: {ex.Message}", LogLevel.Error);
            Logging.Log(ex.ToString(), LogLevel.Debug);
            return false;
        }

        if (candidates.Count == 0)
        {
            Logging.Log("No /dev/tty.usbserial-* devices found.", LogLevel.Error);
            return false;
        }

        if (candidates.Count > 1)
        {
            Logging.Log($"Multiple ({candidates.Count}) tty.usbserial-* devices found. Using the first one: {candidates[0]}", LogLevel.Warning);
            foreach (var p in candidates)
            {
                Logging.Log($"  - Found: {p}", LogLevel.Warning);
            }
            Logging.Log("Pass --serial-device <path> to select a specific one.", LogLevel.Warning);
        }

        _devicePath = candidates[0];
        _deviceGuid = ComPortGuid;
        Logging.Log($"Selected device: {_devicePath}");
        Logging.Log("  Mode detected: Serial Port (tty.usbserial)");
        return true;
    }

    private bool FindDeviceWindowsSerial()
    {
        Logging.Log("Searching for Qualcomm EDL device on Windows (Qualcomm Serial Driver / COM port)...");

        var potentialDevices = new List<(string DevicePath, string BusName, int DevInst)>();
        foreach (var (pathName, busName, devInst) in UsbExtensions.GetDeviceInfos(ComPortGuid))
        {
            Logging.Log($"Found device via COMPortGuid: {pathName} on bus {busName} (DevInst: {devInst})", LogLevel.Debug);
            if (pathName is not null && IsQualcommEdlDevice(pathName, busName))
            {
                potentialDevices.Add((pathName, busName, devInst));
            }
        }

        var uniqueEdlDevices = potentialDevices
            .GroupBy(d => d.DevInst)
            .Select(g => g.First())
            .ToList();

        if (uniqueEdlDevices.Count == 0)
        {
            Logging.Log("No Qualcomm EDL serial devices found. If the device is bound to WinUSB, select the LibUsb backend instead.", LogLevel.Error);
            return false;
        }

        if (uniqueEdlDevices.Count > 1)
        {
            Logging.Log($"Multiple ({uniqueEdlDevices.Count}) unique EDL serial devices found.", LogLevel.Warning);
            foreach (var (devicePath, busName, _) in uniqueEdlDevices)
            {
                Logging.Log($"  - Path: {devicePath}, Bus: {busName}", LogLevel.Warning);
            }
            Logging.Log($"Picking the first device: {uniqueEdlDevices[0].DevicePath}", LogLevel.Warning);
        }

        var (finalDevicePath, finalBusName, finalDevInst) = uniqueEdlDevices.First();
        _devicePath = finalDevicePath;
        _deviceGuid = ComPortGuid;

        Logging.Log($"Found device: {_devicePath}");
        Logging.Log("  Interface: Serial Port");
        Logging.Log($"  Bus Name: {finalBusName}", LogLevel.Debug);
        Logging.Log($"  DevInst: {finalDevInst}", LogLevel.Debug);

        if (finalBusName?.StartsWith("QUSB_BULK", StringComparison.OrdinalIgnoreCase) == true ||
            finalBusName == "QHSUSB_DLOAD" ||
            finalBusName == "QHSUSB__BULK")
        {
            Logging.Log("  Mode detected: Sahara/Firehose");
        }
        else if (finalBusName == "QHSUSB_ARMPRG")
        {
            Logging.Log("  Mode detected: Emergency Flash (9006/other)", LogLevel.Warning);
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
                _serialPort?.Dispose();
                _serialPort = new(ResolveTransportPath());
                CapturePinnedUsbSerial();
                _firehoseClient = new(_serialPort);
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
                _serialPort = null;
                _firehoseClient = null;
                _saharaClient = null;
                CurrentMode = DeviceMode.Unknown;
                if (!FindDevice()) // Find the potentially new device path
                {
                    throw new TodoException(
                        _pinnedUsbSerial != null
                            ? $"Device did not re-enumerate in Firehose mode, or pinned device (iSerial='{_pinnedUsbSerial}') could not be uniquely re-matched among the visible EDL devices. Refusing to pick a different device."
                            : "Device did not re-enumerate in Firehose mode after loader upload, or could not be found.");
                }
                // Now establish the Firehose connection
                Logging.Log("Connecting to re-enumerated device in Firehose mode...", LogLevel.Debug);
                _serialPort = new(ResolveTransportPath());
                CapturePinnedUsbSerial();
                _firehoseClient = new(_serialPort);

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
            _serialPort = new(ResolveTransportPath());
            CapturePinnedUsbSerial();
            _saharaClient = new(_serialPort);
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

            var success = await Task.Run(() => _saharaClient.LoadProgrammer(globalOptions.LoaderPath));

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
                _serialPort?.Dispose();
                _hostDeviceManager?.Dispose();
                _radxaWoSManager?.Dispose();
            }

            _serialPort = null;
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