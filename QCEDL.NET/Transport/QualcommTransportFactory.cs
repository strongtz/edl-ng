namespace Qualcomm.EmergencyDownload.Transport;

public static class QualcommTransportFactory
{
    public static IQualcommTransport Open(string target, TransportBackend backend)
    {
        return backend switch
        {
            TransportBackend.WindowsQud => new WindowsQudTransport(target),
            TransportBackend.LibUsb => new LibUsbTransport(target),
            _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, null)
        };
    }
}