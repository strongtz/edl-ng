using Qualcomm.EmergencyDownload.Transport;

namespace Qualcomm.EmergencyDownload.Layers.PBL.Sahara.Command;

internal sealed class Execute
{
    private static byte[] BuildExecutePacket(uint RequestID)
    {
        var Execute = new byte[0x04];
        ByteOperations.WriteUInt32(Execute, 0x00, RequestID);
        return QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.Execute, Execute);
    }

    private static byte[] BuildExecuteDataPacket(uint RequestID)
    {
        var Execute = new byte[0x04];
        ByteOperations.WriteUInt32(Execute, 0x00, RequestID);
        return QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.ExecuteData, Execute);
    }

    private static byte[] GetCommandVariable(QualcommSerial Serial, QualcommSaharaExecuteCommand command)
    {
        Serial.SendData(BuildExecutePacket((uint)command));

        var ReadDataRequest = Serial.GetResponse(null);
        var ResponseID = ByteOperations.ReadUInt32(ReadDataRequest, 0);

        if (ResponseID != 0xE)
        {
            throw new BadConnectionException();
        }

        var DataLength = ByteOperations.ReadUInt32(ReadDataRequest, 0x0C);

        Serial.SendData(BuildExecuteDataPacket((uint)command));

        return Serial.GetResponse(null, Length: (int)DataLength);
    }


    public static byte[][] GetRKHs(QualcommSerial Serial)
    {
        var Response = GetCommandVariable(Serial, QualcommSaharaExecuteCommand.OemPKHashRead);

        List<byte[]> RootKeyHashes = [];

        var Size = 0x20;

        // SHA384
        if (Response.Length % 0x30 == 0)
        {
            Size = 0x30;
        }

        // SHA256
        if (Response.Length % 0x20 == 0)
        {
            Size = 0x20;
        }

        for (var i = 0; i < Response.Length / Size; i++)
        {
            RootKeyHashes.Add(Response[(i * Size)..((i + 1) * Size)]);
        }

        return [.. RootKeyHashes];
    }

    public static byte[] GetRKH(QualcommSerial Serial)
    {
        var RKHs = GetRKHs(Serial);
        return RKHs[0];
    }

    public static byte[] GetHWID(QualcommSerial Serial)
    {
        var Response = GetCommandVariable(Serial, QualcommSaharaExecuteCommand.MsmHWIDRead);
        return [.. Response.Reverse()];
    }

    public static byte[] GetSerialNumber(QualcommSerial Serial)
    {
        var Response = GetCommandVariable(Serial, QualcommSaharaExecuteCommand.SerialNumRead);
        return [.. Response.Reverse()];
    }
}