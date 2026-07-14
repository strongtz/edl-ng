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

    private static byte[] GetCommandVariable(IQualcommTransport transport, QualcommSaharaExecuteCommand command)
    {
        transport.SendData(BuildExecutePacket((uint)command));

        var readDataRequest = transport.GetResponse(null);
        var responseId = ByteOperations.ReadUInt32(readDataRequest, 0);

        if (responseId != 0xE)
        {
            throw new BadConnectionException();
        }

        var dataLength = ByteOperations.ReadUInt32(readDataRequest, 0x0C);

        transport.SendData(BuildExecuteDataPacket((uint)command));

        return transport.GetResponse(null, length: (int)dataLength);
    }


    public static byte[][] GetRkHs(IQualcommTransport transport)
    {
        var response = GetCommandVariable(transport, QualcommSaharaExecuteCommand.OemPkHashRead);

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

    public static byte[] GetRkh(IQualcommTransport transport)
    {
        var rkHs = GetRkHs(transport);
        return rkHs[0];
    }

    public static byte[] GetHwid(IQualcommTransport transport)
    {
        var response = GetCommandVariable(transport, QualcommSaharaExecuteCommand.MsmHwidRead);
        return [.. response.Reverse()];
    }

    public static byte[] GetSerialNumber(IQualcommTransport transport)
    {
        var response = GetCommandVariable(transport, QualcommSaharaExecuteCommand.SerialNumRead);
        return [.. response.Reverse()];
    }
}