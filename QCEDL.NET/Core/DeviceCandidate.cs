namespace Qualcomm.EmergencyDownload.Core;

/// <summary>
/// A single EDL-capable device surfaced by <see cref="EdlManager.EnumerateDevices"/>.
/// </summary>
/// <param name="Id">Opaque stable token. For serial backends it is the tty/COM path
/// (assignable to <see cref="EdlOptions.SerialDevicePath"/>). For LibUsb it is
/// <c>usb:vid_XXXX,pid_YYYY,bus_N,addr_M</c> (assignable to <see cref="EdlOptions.UsbDeviceId"/>).</param>
/// <param name="DisplayName">Short human label, e.g. <c>/dev/ttyUSB0</c> or <c>USB 05C6:9008 @ bus 1 addr 14</c>.</param>
/// <param name="Details">Extra context for tooltips / detail readouts (bus name on Windows, VID/PID hex, etc.).</param>
/// <param name="Backend">Which transport backend this candidate belongs to.</param>
public sealed record DeviceCandidate(
    string Id,
    string DisplayName,
    string Details,
    TransportBackend Backend);