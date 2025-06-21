using Qualcomm.EmergencyDownload.Transport;

namespace Qualcomm.EmergencyDownload.Layers.PBL.Sahara.Command;

internal sealed class Execute
{
    private static byte[] BuildExecutePacket(uint requestId)
    {
        var execute = new byte[0x04];
        ByteOperations.WriteUInt32(execute, 0x00, requestId);
        return QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.Execute, execute);
    }

    private static byte[] BuildExecuteDataPacket(uint requestId)
    {
        var execute = new byte[0x04];
        ByteOperations.WriteUInt32(execute, 0x00, requestId);
        return QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.ExecuteData, execute);
    }

    private static byte[] GetCommandVariable(QualcommSerial serial, QualcommSaharaExecuteCommand command)
    {
        serial.SendData(BuildExecutePacket((uint)command));

        var readDataRequest = serial.GetResponse(null);
        var responseId = ByteOperations.ReadUInt32(readDataRequest, 0);

        if (responseId != 0xE)
        {
            throw new BadConnectionException();
        }

        var dataLength = ByteOperations.ReadUInt32(readDataRequest, 0x0C);

        serial.SendData(BuildExecuteDataPacket((uint)command));

        return serial.GetResponse(null, length: (int)dataLength);
    }


    public static byte[][] GetRkHs(QualcommSerial serial)
    {
        var response = GetCommandVariable(serial, QualcommSaharaExecuteCommand.OemPkHashRead);

        List<byte[]> rootKeyHashes = [];

        var size = 0x20;

        // SHA384
        if (response.Length % 0x30 == 0)
        {
            size = 0x30;
        }

        // SHA256
        if (response.Length % 0x20 == 0)
        {
            size = 0x20;
        }

        for (var i = 0; i < response.Length / size; i++)
        {
            rootKeyHashes.Add(response[(i * size)..((i + 1) * size)]);
        }

        return [.. rootKeyHashes];
    }

    public static byte[] GetRkh(QualcommSerial serial)
    {
        var rkHs = GetRkHs(serial);
        return rkHs[0];
    }

    public static byte[] GetHwid(QualcommSerial serial)
    {
        var response = GetCommandVariable(serial, QualcommSaharaExecuteCommand.MsmHwidRead);
        return [.. response.Reverse()];
    }

    public static byte[] GetSerialNumber(QualcommSerial serial)
    {
        var response = GetCommandVariable(serial, QualcommSaharaExecuteCommand.SerialNumRead);
        return [.. response.Reverse()];
    }
}