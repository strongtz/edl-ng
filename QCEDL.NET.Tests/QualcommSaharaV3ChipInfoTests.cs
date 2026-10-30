using Qualcomm.EmergencyDownload.Layers.PBL.Sahara;
using Qualcomm.EmergencyDownload.Layers.PBL.Sahara.Command;
using Qualcomm.EmergencyDownload.Transport;

namespace QCEDL.NET.Tests;

public sealed class QualcommSaharaV3ChipInfoTests
{
    [Fact]
    public void ParseReadsProtocol31FuseFieldsAndBuildsLegacyHwid()
    {
        var payload = BuildPayload();

        var chipInfo = QualcommSaharaV3ChipInfo.Parse(payload);

        Assert.Equal(0x00030001u, chipInfo.BinaryVersion);
        Assert.Equal(0x00000002u, chipInfo.TmeFirmwareQtiVersion);
        Assert.Equal(0x00000003u, chipInfo.TmeFirmwareOemVersion);
        Assert.Equal(0x00000004u, chipInfo.XblSecureCoreQtiVersion);
        Assert.Equal(0x00000005u, chipInfo.XblSecureCoreOemVersion);
        Assert.Equal(0x00000006u, chipInfo.XblSecureCoreExtendedOemVersion);
        Assert.Equal(0x00000007u, chipInfo.DeviceProgrammerOemVersion);
        Assert.Equal(0x00000008u, chipInfo.XblConfigOemVersion);
        Assert.Equal(0x60000000u, chipInfo.SocHardwareVersion);
        Assert.Equal(0x001B30E1u, chipInfo.JtagId);
        Assert.Equal(0xA0120051u, chipInfo.RawOemId);
        Assert.Equal(0x11223344u, chipInfo.ProductId);
        Assert.Equal(0x0000000Au, chipInfo.OemLifeCycleState);
        Assert.Equal(0x0000000Bu, chipInfo.MrcActivationList);
        Assert.Equal(0x0000000Cu, chipInfo.MrcRevocationList);
        Assert.Equal(0x0000000Du, chipInfo.NumberOfRootCertificates);
        Assert.Equal(0x0000000Eu, chipInfo.AppsSecureDebugStatus);
        Assert.Equal(0x0000000Fu, chipInfo.PublicKeyHashInFuse);
        Assert.Equal(0x00000010u, chipInfo.OemAuthenticationEnabled);
        Assert.Equal(0x00000011u, chipInfo.RomPublicKeyHashIndex);
        Assert.Equal((ushort)0x0051, chipInfo.OemId);
        Assert.Equal((ushort)0xA012, chipInfo.ModelId);
        Assert.Equal(0x001B30E10051A012ul, chipInfo.Hwid);
        Assert.Equal("001B30E10051A012", Convert.ToHexString(chipInfo.ToHwidBytes()));
    }

    [Fact]
    public void ParseUsesProductIdAsQdlCompatibleOemFallback()
    {
        var payload = BuildPayload();
        ByteOperations.WriteUInt32(payload, 0x28, 0xA0120000);
        ByteOperations.WriteUInt32(payload, 0x2C, 0x00000051);

        var chipInfo = QualcommSaharaV3ChipInfo.Parse(payload);

        Assert.Equal((ushort)0x0051, chipInfo.OemId);
        Assert.Equal((ushort)0xA012, chipInfo.ModelId);
        Assert.Equal(0x001B30E10051A012ul, chipInfo.Hwid);
    }

    [Fact]
    public void ParseAcceptsMinimumQdlPayloadWithoutOptionalFields()
    {
        var payload = BuildPayload()[..QualcommSaharaV3ChipInfo.MinimumHwidPayloadLength];

        var chipInfo = QualcommSaharaV3ChipInfo.Parse(payload);

        Assert.Null(chipInfo.ProductId);
        Assert.Null(chipInfo.OemLifeCycleState);
        Assert.Equal(0x001B30E10051A012ul, chipInfo.Hwid);
    }

    [Theory]
    [InlineData(40)]
    [InlineData(45)]
    public void ParseRejectsMissingOrUnalignedHwidFields(int payloadLength)
    {
        _ = Assert.Throws<BadMessageException>(() =>
            QualcommSaharaV3ChipInfo.Parse(new byte[payloadLength]));
    }

    [Fact]
    public void ExecuteRequestsCmd10AndReadsFragmentedPayload()
    {
        var payload = BuildPayload();
        var responses = new Queue<byte[]>(
        [
            BuildExecuteResponse(QualcommSaharaExecuteCommand.ReadChipIdV3, payload.Length),
            payload[..32],
            payload[32..]
        ]);
        using var transport = new SaharaRecordingTransport(responses);

        var chipInfo = Execute.GetV3ChipInfo(transport);

        Assert.Equal(0x001B30E10051A012ul, chipInfo.Hwid);
        Assert.Equal(
            [QualcommSaharaCommand.Execute, QualcommSaharaCommand.ExecuteData],
            transport.SentCommands);
        Assert.All(transport.SentPackets, packet =>
            Assert.Equal((uint)QualcommSaharaExecuteCommand.ReadChipIdV3, BitConverter.ToUInt32(packet, 0x08)));
        Assert.Empty(responses);
    }

    [Fact]
    public void GetHwidAutomaticallyUsesCmd10ForProtocolV3()
    {
        var payload = BuildPayload();
        var responses = new Queue<byte[]>(
        [
            QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.CommandReady),
            BuildExecuteResponse(QualcommSaharaExecuteCommand.ReadChipIdV3, payload.Length),
            payload
        ]);
        using var transport = new SaharaRecordingTransport(responses);
        var sahara = new QualcommSahara(transport);
        var hello = QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.Hello, new byte[40]);
        ByteOperations.WriteUInt32(hello, 0x08, 3);

        Assert.Equal(QualcommSaharaHandshakeResult.Sahara, sahara.ProbeCommandMode(hello));
        var hwid = sahara.GetHwid();

        Assert.Equal("001B30E10051A012", Convert.ToHexString(hwid));
        Assert.Equal(
            [
                QualcommSaharaCommand.HelloResponse,
                QualcommSaharaCommand.Execute,
                QualcommSaharaCommand.ExecuteData
            ],
            transport.SentCommands);
        Assert.Empty(responses);
    }

    [Fact]
    public void SerialNumberReadUsesLowDwordOnProtocolV3Response()
    {
        var payload = new byte[8];
        ByteOperations.WriteUInt32(payload, 0x00, 0x0A76AFF4);
        ByteOperations.WriteUInt32(payload, 0x04, 0x00000437);
        var responses = new Queue<byte[]>(
        [
            BuildExecuteResponse(QualcommSaharaExecuteCommand.SerialNumRead, payload.Length),
            payload
        ]);
        using var transport = new SaharaRecordingTransport(responses);

        var serialNumber = Execute.GetSerialNumber(transport);

        Assert.Equal("0A76AFF4", Convert.ToHexString(serialNumber));
        Assert.Empty(responses);
    }

    [Fact]
    public void DiscardedHelloInfersV3FromSerialResponseAndUsesCmd10()
    {
        var serialPayload = new byte[8];
        ByteOperations.WriteUInt32(serialPayload, 0x00, 0x726DDCAB);
        ByteOperations.WriteUInt32(serialPayload, 0x04, 0x00000437);
        var chipInfoPayload = BuildPayload();
        var responses = new Queue<byte[]>(
        [
            QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.CommandReady),
            BuildExecuteResponse(QualcommSaharaExecuteCommand.SerialNumRead, serialPayload.Length),
            serialPayload,
            BuildExecuteResponse(QualcommSaharaExecuteCommand.ReadChipIdV3, chipInfoPayload.Length),
            chipInfoPayload
        ]);
        using var transport = new SaharaRecordingTransport(responses, initialReadTimeouts: 1);
        var sahara = new QualcommSahara(transport);

        Assert.Equal(QualcommSaharaHandshakeResult.Sahara, sahara.ProbeCommandMode());
        Assert.False(sahara.HasDetectedDeviceSaharaVersion);

        Assert.Equal("726DDCAB", Convert.ToHexString(sahara.GetSerialNumber()));
        Assert.Equal(3u, sahara.DetectedDeviceSaharaVersion);
        Assert.True(sahara.HasDetectedDeviceSaharaVersion);
        Assert.True(sahara.IsDeviceSaharaVersionInferred);
        Assert.Equal(0x00000437u, sahara.DetectedDeviceChipId);
        Assert.Equal("001B30E10051A012", Convert.ToHexString(sahara.GetHwid()));

        Assert.Equal(
            [
                QualcommSaharaExecuteCommand.SerialNumRead,
                QualcommSaharaExecuteCommand.SerialNumRead,
                QualcommSaharaExecuteCommand.ReadChipIdV3,
                QualcommSaharaExecuteCommand.ReadChipIdV3
            ],
            transport.SentPackets
                .Where(static packet =>
                    BitConverter.ToUInt32(packet, 0) is
                        (uint)QualcommSaharaCommand.Execute or
                        (uint)QualcommSaharaCommand.ExecuteData)
                .Select(static packet =>
                    (QualcommSaharaExecuteCommand)BitConverter.ToUInt32(packet, 0x08)));
        Assert.Empty(responses);
    }

    [Fact]
    public void DiscardedHelloKeepsBaselineVersionForFourByteSerialResponse()
    {
        var serialPayload = new byte[4];
        ByteOperations.WriteUInt32(serialPayload, 0x00, 0x726DDCAB);
        var responses = new Queue<byte[]>(
        [
            QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.CommandReady),
            BuildExecuteResponse(QualcommSaharaExecuteCommand.SerialNumRead, serialPayload.Length),
            serialPayload
        ]);
        using var transport = new SaharaRecordingTransport(responses, initialReadTimeouts: 1);
        var sahara = new QualcommSahara(transport);

        Assert.Equal(QualcommSaharaHandshakeResult.Sahara, sahara.ProbeCommandMode());
        Assert.Equal("726DDCAB", Convert.ToHexString(sahara.GetSerialNumber()));

        Assert.Equal(2u, sahara.DetectedDeviceSaharaVersion);
        Assert.False(sahara.HasDetectedDeviceSaharaVersion);
        Assert.False(sahara.IsDeviceSaharaVersionInferred);
        Assert.Null(sahara.DetectedDeviceChipId);
        Assert.Empty(responses);
    }

    [Fact]
    public void ExecuteReportsEndImageTxStatusInsteadOfMisparsingExecuteFields()
    {
        var responses = new Queue<byte[]>(
        [
            BuildEndImageTxPacket(13, 0x1F)
        ]);
        using var transport = new SaharaRecordingTransport(responses);

        var ex = Assert.Throws<QualcommSaharaUnexpectedPacketException>(() =>
            Execute.GetHwid(transport));

        Assert.Equal(QualcommSaharaCommand.EndImageTx, ex.ReceivedCommand);
        Assert.Equal(13u, ex.ImageId);
        Assert.Equal(0x1Fu, ex.Status);
        Assert.Contains("ErrorExecCmdUnsupported", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("client command 13", ex.Message, StringComparison.Ordinal);
        Assert.Equal([QualcommSaharaCommand.Execute], transport.SentCommands);
        Assert.Empty(responses);
    }

    private static byte[] BuildPayload()
    {
        var payload = new byte[0x50];
        ByteOperations.WriteUInt32(payload, 0x00, 0x00030001);
        ByteOperations.WriteUInt32(payload, 0x04, 0x00000002);
        ByteOperations.WriteUInt32(payload, 0x08, 0x00000003);
        ByteOperations.WriteUInt32(payload, 0x0C, 0x00000004);
        ByteOperations.WriteUInt32(payload, 0x10, 0x00000005);
        ByteOperations.WriteUInt32(payload, 0x14, 0x00000006);
        ByteOperations.WriteUInt32(payload, 0x18, 0x00000007);
        ByteOperations.WriteUInt32(payload, 0x1C, 0x00000008);
        ByteOperations.WriteUInt32(payload, 0x20, 0x60000000);
        ByteOperations.WriteUInt32(payload, 0x24, 0x001B30E1);
        ByteOperations.WriteUInt32(payload, 0x28, 0xA0120051);
        ByteOperations.WriteUInt32(payload, 0x2C, 0x11223344);
        ByteOperations.WriteUInt32(payload, 0x30, 0x0000000A);
        ByteOperations.WriteUInt32(payload, 0x34, 0x0000000B);
        ByteOperations.WriteUInt32(payload, 0x38, 0x0000000C);
        ByteOperations.WriteUInt32(payload, 0x3C, 0x0000000D);
        ByteOperations.WriteUInt32(payload, 0x40, 0x0000000E);
        ByteOperations.WriteUInt32(payload, 0x44, 0x0000000F);
        ByteOperations.WriteUInt32(payload, 0x48, 0x00000010);
        ByteOperations.WriteUInt32(payload, 0x4C, 0x00000011);
        return payload;
    }

    private static byte[] BuildExecuteResponse(QualcommSaharaExecuteCommand command, int payloadLength)
    {
        var payload = new byte[8];
        ByteOperations.WriteUInt32(payload, 0x00, (uint)command);
        ByteOperations.WriteUInt32(payload, 0x04, checked((uint)payloadLength));
        return QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.ExecuteResponse, payload);
    }

    private static byte[] BuildEndImageTxPacket(uint imageId, uint status)
    {
        var payload = new byte[8];
        ByteOperations.WriteUInt32(payload, 0x00, imageId);
        ByteOperations.WriteUInt32(payload, 0x04, status);
        return QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.EndImageTx, payload);
    }
}
