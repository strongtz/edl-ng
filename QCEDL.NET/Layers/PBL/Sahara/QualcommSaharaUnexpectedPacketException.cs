using Qualcomm.EmergencyDownload.Transport;

namespace Qualcomm.EmergencyDownload.Layers.PBL.Sahara;

public sealed class QualcommSaharaUnexpectedPacketException : BadMessageException
{
    internal QualcommSaharaUnexpectedPacketException(
        string message,
        QualcommSaharaCommand receivedCommand,
        byte[] rawPacket,
        uint? imageId = null,
        uint? status = null) : base(message)
    {
        ReceivedCommand = receivedCommand;
        RawPacket = rawPacket;
        ImageId = imageId;
        Status = status;
    }

    public QualcommSaharaCommand ReceivedCommand { get; }
    public byte[] RawPacket { get; }
    public uint? ImageId { get; }
    public uint? Status { get; }
}
