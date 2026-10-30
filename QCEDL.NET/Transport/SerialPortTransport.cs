using System.IO.Ports;

namespace Qualcomm.EmergencyDownload.Transport;

public sealed class SerialPortTransport : IQualcommTransport
{
    private readonly SerialPort _port;

    public SerialPortTransport(string path)
    {
        _port = new SerialPort(path, 115200) { ReadTimeout = 1000, WriteTimeout = 1000 };
        try
        {
            _port.Open();
        }
        catch
        {
            _port.Dispose();
            throw;
        }
    }

    public TransportBackend Backend => TransportBackend.Serial;

    public int TimeoutMilliseconds
    {
        get => _port.ReadTimeout;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _port.ReadTimeout = Math.Max(value, 1);
            _port.WriteTimeout = Math.Max(value, 1);
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        return _port.Read(buffer, offset, count);
    }

    public int Write(byte[] buffer, int offset, int count)
    {
        _port.Write(buffer, offset, count);
        return count;
    }

    public void SendZeroLengthPacket()
    {
        // Serial ports expose a byte stream, without USB packet boundaries.
    }

    public void Dispose()
    {
        _port.Dispose();
    }
}
