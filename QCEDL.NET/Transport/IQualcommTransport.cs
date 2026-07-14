namespace Qualcomm.EmergencyDownload.Transport;

public enum TransportBackend
{
    WindowsQud,
    LibUsb
}

public interface IQualcommTransport : IDisposable
{
    TransportBackend Backend { get; }

    int TimeoutMilliseconds { get; set; }

    int Read(byte[] buffer, int offset, int count);

    int Write(byte[] buffer, int offset, int count);

    void SendZeroLengthPacket();
}