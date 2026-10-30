using QCEDL.NET.Logging;
using Qualcomm.EmergencyDownload.Transport;

namespace Qualcomm.EmergencyDownload.Layers.PBL.Sahara.Command;

internal sealed class Execute
{
    private const int ExecuteResponsePacketLength = 0x10;
    private const int MaximumExecuteResponsePacketLength = 0x40;
    private const int MaximumCommandResponseLength = 0x10000;

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

        var executeResponse = transport.GetResponse(null, MaximumExecuteResponsePacketLength);
        LibraryLogger.Trace(
            $"Sahara EXECUTE response for {command} ({executeResponse.Length} bytes): {Convert.ToHexString(executeResponse)}");
        if (executeResponse.Length < 0x08)
        {
            throw new BadMessageException(
                $"Sahara EXECUTE response for {command} is {executeResponse.Length} bytes; at least 8 bytes are required.");
        }

        var responseId = (QualcommSaharaCommand)ByteOperations.ReadUInt32(executeResponse, 0x00);
        var declaredLength = ByteOperations.ReadUInt32(executeResponse, 0x04);
        ThrowIfUnexpectedSaharaPacket(command, executeResponse, responseId, declaredLength);
        if (executeResponse.Length != ExecuteResponsePacketLength)
        {
            throw new BadMessageException(
                $"Sahara EXECUTE response for {command} is {executeResponse.Length} bytes; expected {ExecuteResponsePacketLength} bytes.");
        }

        var responseCommand = (QualcommSaharaExecuteCommand)ByteOperations.ReadUInt32(executeResponse, 0x08);
        var dataLength = ByteOperations.ReadUInt32(executeResponse, 0x0C);
        if (responseId != QualcommSaharaCommand.ExecuteResponse ||
            declaredLength != ExecuteResponsePacketLength || responseCommand != command)
        {
            throw new BadMessageException(
                $"Invalid Sahara EXECUTE response for {command}: response {responseId}, declared length {declaredLength}, client command {responseCommand}.");
        }

        if (dataLength is 0 or > MaximumCommandResponseLength)
        {
            throw new BadMessageException(
                $"Sahara EXECUTE response for {command} reported invalid payload length {dataLength}.");
        }

        transport.SendData(BuildExecuteDataPacket((uint)command));

        var response = ReadExactly(transport, checked((int)dataLength));
        LibraryLogger.Trace(
            $"Sahara EXECUTE payload for {command} ({response.Length} bytes): {Convert.ToHexString(response)}");
        return response;
    }

    private static void ThrowIfUnexpectedSaharaPacket(
        QualcommSaharaExecuteCommand clientCommand,
        byte[] packet,
        QualcommSaharaCommand responseCommand,
        uint declaredLength)
    {
        if (responseCommand == QualcommSaharaCommand.ExecuteResponse ||
            responseCommand == QualcommSaharaCommand.NoCommand ||
            !Enum.IsDefined(responseCommand) ||
            declaredLength != packet.Length)
        {
            return;
        }

        uint? imageId = null;
        uint? status = null;
        var details = string.Empty;
        if (responseCommand == QualcommSaharaCommand.EndImageTx && packet.Length == 0x10)
        {
            imageId = ByteOperations.ReadUInt32(packet, 0x08);
            status = ByteOperations.ReadUInt32(packet, 0x0C);
            var statusCode = (QualcommSaharaStatusCode)status.Value;
            var statusDescription = Enum.IsDefined(statusCode)
                ? $"{statusCode} (0x{status.Value:X8})"
                : $"Unknown (0x{status.Value:X8})";
            details = $" (Image ID {imageId.Value}, Status {statusDescription})";
        }

        throw new QualcommSaharaUnexpectedPacketException(
            $"Expected Sahara EXECUTE_RESP for {clientCommand}, received {responseCommand}{details}.",
            responseCommand,
            [.. packet],
            imageId,
            status);
    }

    private static byte[] ReadExactly(IQualcommTransport transport, int length)
    {
        var response = new byte[length];
        var offset = 0;
        while (offset < response.Length)
        {
            var bytesRead = transport.Read(response, offset, response.Length - offset);
            if (bytesRead == 0)
            {
                throw new BadMessageException(
                    $"Device returned an empty Sahara EXECUTE payload after {offset} of {length} bytes.");
            }

            offset += bytesRead;
        }

        return response;
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

    public static QualcommSaharaV3ChipInfo GetV3ChipInfo(IQualcommTransport transport)
    {
        var response = GetCommandVariable(transport, QualcommSaharaExecuteCommand.ReadChipIdV3);
        return QualcommSaharaV3ChipInfo.Parse(response);
    }

    public static byte[] GetSerialNumber(IQualcommTransport transport)
    {
        return GetSerialNumber(transport, out _);
    }

    public static byte[] GetSerialNumber(IQualcommTransport transport, out uint? chipId)
    {
        var response = GetCommandVariable(transport, QualcommSaharaExecuteCommand.SerialNumRead);
        chipId = response.Length >= 2 * sizeof(uint)
            ? ByteOperations.ReadUInt32(response, sizeof(uint))
            : null;
        return response.Length >= sizeof(uint)
            ? [response[3], response[2], response[1], response[0]]
            : throw new BadMessageException(
                $"Sahara serial-number response is {response.Length} bytes; expected at least {sizeof(uint)} bytes.");
    }
}
