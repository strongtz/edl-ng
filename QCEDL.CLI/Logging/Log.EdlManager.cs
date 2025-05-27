using Microsoft.Extensions.Logging;
using QCEDL.CLI.Core;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace QCEDL.CLI.Logging;

internal static partial class Log
{
    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Using cached device mode: {mode}")]
    internal static partial void UsingCachedDeviceMode(
        this ILogger<EdlManager> logger,
        DeviceMode mode);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Cannot detect mode: No device found.")]
    internal static partial void CannotDetectModeNoDeviceFound(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Probing device mode...")]
    internal static partial void ProbingDeviceMode(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Attempting passive read...")]
    internal static partial void AttemptingPassiveRead(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Reading initial data from device...")]
    internal static partial void ReadingInitialData(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Initial read completed.")]
    internal static partial void InitialReadCompleted(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Passive read received potentially incomplete/bad message.")]
    internal static partial void PassiveReadReceivedIncompleteBadMessage(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Passive read received potentially incomplete/bad message.")]
    public static partial void PassiveReadReceivedPotentiallyIncompleteBadMessage(this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Passive read got {length} bytes: {hexString}")]
    public static partial void PassiveReadGotBytes(this ILogger<EdlManager> logger, int length, string hexString);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Passive read matches Sahara HELLO pattern. Detected Mode: Sahara")]
    public static partial void PassiveReadMatchesSaharaHelloPattern(this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Passive read contains XML declaration. Detected Mode: Firehose")]
    public static partial void PassiveReadContainsXmlDeclaration(this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Passive read received unexpected data.")]
    public static partial void PassiveReadReceivedUnexpectedData(this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "No serial port available for Firehose NOP probe.")]
    public static partial void NoSerialPortAvailableForFirehoseNopProbe(this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Passive read inconclusive. Attempting active Firehose NOP probe...")]
    public static partial void PassiveReadInconclusiveAttemptingActiveFirehoseNopProbe(this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Firehose NOP probe successful. Detected Mode: Firehose")]
    public static partial void FirehoseNopProbeSuccessful(this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Flushing serial output...")]
    public static partial void FlushingSerialOutput(this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "DEVPRG LOG: {logValue}")]
    public static partial void DevprgLog(
        this ILogger<EdlManager> logger,
        string? logValue);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Firehose NOP probe: No XML response received.")]
    public static partial void FirehoseNopProbeNoXmlResponseReceived(this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Firehose NOP probe timed out.")]
    public static partial void FirehoseNopProbeTimedOut(
        this ILogger<EdlManager> logger,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Firehose NOP probe failed (bad connection).")]
    public static partial void FirehoseNopProbeFailedBadConnection(
        this ILogger<EdlManager> logger,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Firehose NOP probe failed (bad message).")]
    public static partial void FirehoseNopProbeFailedBadMessage(
        this ILogger<EdlManager> logger,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Firehose NOP probe failed unexpectedly.")]
    public static partial void FirehoseNopProbeFailedUnexpectedly(
        this ILogger<EdlManager> logger,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "No serial port available for Sahara handshake probe.")]
    public static partial void NoSerialPortAvailable(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Probes inconclusive. Attempting *full* Sahara handshake as last resort...")]
    public static partial void ProbesInconclusiveAttemptingFullHandshake(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Full Sahara handshake probe successful. Detected Mode: Sahara")]
    public static partial void FullSaharaHandshakeProbeSuccessfulDetectedMode(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Full Sahara handshake probe failed.")]
    public static partial void FullSaharaHandshakeProbeFailed(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Full Sahara handshake probe timed out.")]
    public static partial void FullSaharaHandshakeProbeTimedOut(
        this ILogger<EdlManager> logger,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Full Sahara handshake probe failed (bad connection).")]
    public static partial void FullSaharaHandshakeProbeFailedBadConnection(
        this ILogger<EdlManager> logger,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Full Sahara handshake probe failed (bad message - likely not Sahara).")]
    public static partial void FullSaharaHandshakeProbeFailedBadMessageLikelyNotSahara(
        this ILogger<EdlManager> logger,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Full Sahara handshake probe failed unexpectedly.")]
    public static partial void FullSaharaHandshakeProbeFailedUnexpectedly(
        this ILogger<EdlManager> logger,
        Exception ex);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error during mode detection.")]
    public static partial void ErrorDuringModeDetection(
        this ILogger<EdlManager> logger,
        Exception ex);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Detected device mode: {mode}")]
    public static partial void DetectedDeviceMode(
        this ILogger<EdlManager> logger,
        DeviceMode mode);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Searching for Qualcomm EDL device...")]
    public static partial void SearchingForQualcommEdlDevice(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Found device using LibUsbDotNet on Linux / MacOS.")]
    public static partial void FoundDeviceUsingLibUsbDotNetOnLinuxOrMacOS(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message =
            "LibUsbDotNet device detection failed or no device found. Falling back to Serial Port (ttyUSB/ttyACM) detection on Linux...")]
    public static partial void LibUsbDotNetDeviceDetectionFailedFallbackToSerialPortOnLinux(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message =
            "Serial Port is known to be broken on Linux. Please double check why LibUsb is not working if device detection succeeds with Serial Port.")]
    public static partial void
        SerialPortKnownBrokenOnLinuxPleaseDoubleCheckWhyLibUsbNotWorkingIfDetectionSucceedsWithSerialPort(
            this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Unsupported OS: {osDescription}. Device discovery skipped.")]
    public static partial void UnsupportedOsDeviceDiscoverySkipped(
        this ILogger<EdlManager> logger,
        string osDescription);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Trying to find device using LibUsbDotNet on Linux / MacOS...")]
    public static partial void TryingToFindDeviceUsingLibUsbDotNetOnLinuxOrMacOS(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "LibUsbDotNet context not initialized. Cannot use LibUsb backend.")]
    public static partial void LibUsbDotNetContextNotInitializedCannotUseLibUsbBackend(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "LibUsbDotNet found device: VID={vid:X4}, PID={pid:X4}. Path set to: {path}")]
    public static partial void LibUsbDotNetFoundDevice(
        this ILogger<EdlManager> logger,
        int vid,
        int pid,
        string path);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "LibUsbDotNet: No device found with VID={vid:X4}, PID={pid:X4}.")]
    public static partial void LibUsbDotNetNoDeviceFound(
        this ILogger<EdlManager> logger,
        int vid,
        int pid);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error during LibUsbDotNet device discovery on Linux.")]
    public static partial void ErrorDuringLibUsbDotNetDeviceDiscoveryOnLinux(
        this ILogger<EdlManager> logger,
        Exception ex);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Searching for Qualcomm EDL device on Linux (/dev/ttyUSB* or /dev/ttyACM*)...")]
    public static partial void SearchingForQualcommEdlDeviceOnLinuxOrMacOS(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Target VID: {targetVid}, PID: {targetPid}")]
    public static partial void TargetVidPid(
        this ILogger<EdlManager> logger,
        string targetVid,
        string targetPid);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Sysfs tty path not found: {sysTtyPath}")]
    public static partial void SysfsTtyPathNotFound(
        this ILogger<EdlManager> logger,
        string sysTtyPath);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "TTY dirName: {dirName}")]
    public static partial void TtyDirName(
        this ILogger<EdlManager> logger,
        string dirName);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Found TTY: {ttyName}, Real Device Path: {realDevicePath}")]
    public static partial void FoundTtyRealDevicePath(
        this ILogger<EdlManager> logger,
        string ttyName,
        string realDevicePath);

    [LoggerMessage(
        Level = LogLevel.Trace,
        Message = "Found TTY: {ttyName}, VID: {vid}, PID: {pid}")]
    public static partial void FoundTtyVidPid(
        this ILogger<EdlManager> logger,
        string ttyName,
        string vid,
        string pid);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Match: {devPath} for VID/PID {targetVid}/{targetPid}")]
    public static partial void MatchForVidPid(
        this ILogger<EdlManager> logger,
        string devPath,
        string targetVid,
        string targetPid);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error during Linux device discovery.")]
    public static partial void ErrorDuringLinuxDeviceDiscovery(
        this ILogger<EdlManager> logger,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "No matching Qualcomm EDL serial devices found.")]
    public static partial void NoMatchingSerialDevicesFound(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Multiple ({count}) matching serial devices found. Using the first one: {firstDevice}")]
    public static partial void MultipleMatchingSerialDevicesFound(
        this ILogger<EdlManager> logger,
        int count,
        string firstDevice);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "  - Found: {device}")]
    public static partial void FoundDeviceListItem(
        this ILogger<EdlManager> logger,
        string device);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Selected device: {devicePath}")]
    public static partial void SelectedDevice(
        this ILogger<EdlManager> logger,
        string devicePath);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Mode detected: Serial Port (ttyUSB/ttyACM)")]
    public static partial void ModeDetectedSerialPort(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Searching for Qualcomm EDL device on Windows (Qualcomm Serial Driver or WinUSB)...")]
    public static partial void SearchingForQualcommEdlDeviceOnWindows(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Found device via COMPortGuid: {pathName} on bus {busName} (DevInst: {devInst})")]
    public static partial void FoundDeviceViaComPortGuid(
        this ILogger<EdlManager> logger,
        string? pathName,
        string busName,
        int devInst);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Found device via WinUSBGuid: {pathName} on bus {busName} (DevInst: {devInst})")]
    public static partial void FoundDeviceViaWinUsbGuid(
        this ILogger<EdlManager> logger,
        string? pathName,
        string busName,
        int devInst);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Selecting device (DevInst: {devInst}) via COMPortGuid: {devicePath}")]
    public static partial void SelectingDeviceViaComPortGuid(
        this ILogger<EdlManager> logger,
        int devInst,
        string devicePath);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Selecting device (DevInst: {devInst}) via {interfaceType}: {devicePath}")]
    public static partial void SelectingDeviceViaInterface(
        this ILogger<EdlManager> logger,
        int devInst,
        string interfaceType,
        string devicePath);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "No Qualcomm EDL devices found.")]
    public static partial void NoQualcommEdlDevicesFound(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Multiple ({count}) unique EDL devices found. Please specify which device to use.")]
    public static partial void MultipleUniqueEdlDevicesFound(
        this ILogger<EdlManager> logger,
        int count);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "  - Path: {devicePath}, Bus: {busName}, Interface: {interfaceType}")]
    public static partial void UniqueEdlDeviceListItem(
        this ILogger<EdlManager> logger,
        string devicePath,
        string busName,
        string interfaceType);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Picking the first device: {devicePath}")]
    public static partial void PickingFirstDevice(
        this ILogger<EdlManager> logger,
        string devicePath);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message =
            "Qualcomm EDL device selected: {devicePath} on bus {busName} (Interface: {interfaceType}, DevInst: {devInst})")]
    public static partial void QualcommEdlDeviceSelected(
        this ILogger<EdlManager> logger,
        string devicePath,
        string busName,
        string interfaceType,
        int devInst);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Found device: {devicePath}")]
    public static partial void FoundDevice(
        this ILogger<EdlManager> logger,
        string devicePath);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "  Interface: {interfaceType}")]
    public static partial void FoundDeviceInterface(
        this ILogger<EdlManager> logger,
        string interfaceType);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "  Bus Name: {busName}")]
    public static partial void FoundDeviceBusName(
        this ILogger<EdlManager> logger,
        string busName);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "  Mode detected: Sahara/Firehose (9008)")]
    public static partial void ModeDetectedSaharaFirehose(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "  Mode detected: Emergency Flash (9006/other)")]
    public static partial void ModeDetectedEmergencyFlash(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "  Mode detection based on BusName uncertain.")]
    public static partial void ModeDetectionBasedOnBusNameUncertain(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Already in Firehose mode.")]
    public static partial void AlreadyInFirehoseMode(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Device is in Firehose mode. Establishing connection...")]
    public static partial void DeviceInFirehoseEstablishingConnection(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Device is in Sahara mode. Uploading loader...")]
    public static partial void DeviceInSaharaUploadingLoader(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Waiting for device to re-enumerate in Firehose mode...")]
    public static partial void WaitingForReenumerationInFirehose(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Connecting to re-enumerated device in Firehose mode...")]
    public static partial void ConnectingToReenumeratedFirehose(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Sahara client not pre-established, creating new connection.")]
    public static partial void SaharaClientNotPreEstablishedCreatingNewConnection(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Using pre-established Sahara connection.")]
    public static partial void UsingPreEstablishedSaharaConnection(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Device is COM Port and no pre-read HELLO. Sending ResetStateMachine command to device...")]
    public static partial void DeviceIsComPortNoPreReadHelloSendingResetStateMachineCommandToDevice(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to send ResetStateMachine for COM port")]
    public static partial void FailedToSendResetStateMachineForComPort(
        this ILogger<EdlManager> logger,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Attempting Sahara handshake...")]
    public static partial void AttemptingSaharaHandshake(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Initial Sahara handshake failed, attempting reset and retry...")]
    public static partial void InitialSaharaHandshakeFailedAttemptingResetAndRetry(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Sahara handshake successful after reset.")]
    public static partial void SaharaHandshakeSuccessfulAfterReset(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Sahara reset/retry failed")]
    public static partial void SaharaResetRetryFailed(
        this ILogger<EdlManager> logger,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Sahara handshake successful.")]
    public static partial void SaharaHandshakeSuccessful(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Serial Number: {hexString}")]
    public static partial void SerialNumber(
        this ILogger<EdlManager> logger,
        string hexString);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Sahara version < 3, attempting to get HWID and RKH.")]
    public static partial void SaharaVersionLessThan3AttemptingGetHwidAndRkh(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Sahara version >= 3, skipping HWID retrieval.")]
    public static partial void SaharaVersionGreaterOrEqual3SkippingHwidRetrieval(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "RKH[{index}]: {hexValue}")]
    public static partial void RkhAtIndex(
        this ILogger<EdlManager> logger,
        int index,
        string hexValue);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to get device info via Sahara")]
    public static partial void FailedToGetDeviceInfoViaSahara(
        this ILogger<EdlManager> logger,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Switching to image transfer mode...")]
    public static partial void SwitchingToImageTransferMode(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Uploading loader: {loaderPath}")]
    public static partial void UploadingLoader(
        this ILogger<EdlManager> logger,
        string loaderPath);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Loader uploaded and started successfully via Sahara.")]
    public static partial void LoaderUploadedAndStartedSuccessfullyViaSahara(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error during Sahara operations")]
    public static partial void ErrorDuringSaharaOperations(
        this ILogger<EdlManager> logger,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Closing Sahara connection after loader upload attempt.")]
    public static partial void ClosingSaharaConnectionAfterLoaderUploadAttempt(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Sending Firehose configure command...")]
    public static partial void SendingFirehoseConfigureCommand(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Firehose configuration might have failed (check logs).")]
    public static partial void FirehoseConfigurationMightHaveFailedCheckLogs(
        this ILogger<EdlManager> logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Firehose configured for Memory: {storage}, MaxPayload: {maxPayload}\n")]
    public static partial void FirehoseConfiguredForMemoryMaxPayload(
        this ILogger<EdlManager> logger,
        StorageType storage,
        ulong maxPayload);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Error during Firehose configuration")]
    public static partial void ErrorDuringFirehoseConfiguration(
        this ILogger<EdlManager> logger,
        Exception exception);
}