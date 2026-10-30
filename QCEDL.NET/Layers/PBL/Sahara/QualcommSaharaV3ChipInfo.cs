using System.Buffers.Binary;
using Qualcomm.EmergencyDownload.Transport;

namespace Qualcomm.EmergencyDownload.Layers.PBL.Sahara;

public sealed class QualcommSaharaV3ChipInfo
{
    public const int MinimumHwidPayloadLength = 0x2C;

    public uint BinaryVersion { get; private init; }
    public uint TmeFirmwareQtiVersion { get; private init; }
    public uint TmeFirmwareOemVersion { get; private init; }
    public uint XblSecureCoreQtiVersion { get; private init; }
    public uint XblSecureCoreOemVersion { get; private init; }
    public uint XblSecureCoreExtendedOemVersion { get; private init; }
    public uint DeviceProgrammerOemVersion { get; private init; }
    public uint XblConfigOemVersion { get; private init; }
    public uint SocHardwareVersion { get; private init; }
    public uint JtagId { get; private init; }
    public uint RawOemId { get; private init; }
    public uint? ProductId { get; private init; }
    public uint? OemLifeCycleState { get; private init; }
    public uint? MrcActivationList { get; private init; }
    public uint? MrcRevocationList { get; private init; }
    public uint? NumberOfRootCertificates { get; private init; }
    public uint? AppsSecureDebugStatus { get; private init; }
    public uint? PublicKeyHashInFuse { get; private init; }
    public uint? OemAuthenticationEnabled { get; private init; }
    public uint? RomPublicKeyHashIndex { get; private init; }
    public ushort OemId { get; private init; }
    public ushort ModelId { get; private init; }

    public ulong Hwid => ((ulong)JtagId << 32) | ((ulong)OemId << 16) | ModelId;

    public byte[] ToHwidBytes()
    {
        var hwid = new byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(hwid, Hwid);
        return hwid;
    }

    public static QualcommSaharaV3ChipInfo Parse(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return Parse(payload.AsSpan());
    }

    public static QualcommSaharaV3ChipInfo Parse(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < MinimumHwidPayloadLength)
        {
            throw new BadMessageException(
                $"Sahara v3 CMD10 returned {payload.Length} bytes; at least {MinimumHwidPayloadLength} bytes are required to reconstruct the HWID.");
        }

        if (payload.Length % sizeof(uint) != 0)
        {
            throw new BadMessageException(
                $"Sahara v3 CMD10 returned an invalid {payload.Length}-byte payload; fields must be 4-byte aligned.");
        }

        var rawOemId = ReadUInt32(payload, 0x28);
        var productId = ReadOptionalUInt32(payload, 0x2C);

        // For legacy 64-bit HWID compatibility, qdl interprets the low and
        // high halves of CMD10's 32-bit OEM_ID field as OEM_ID and MODEL_ID.
        var oemId = (ushort)(rawOemId & ushort.MaxValue);
        var modelId = (ushort)(rawOemId >> 16);

        // Match qdl's compatibility handling for targets that carry the OEM ID
        // in the low half of the following PRODUCT_ID field.
        if (oemId == 0 && productId.HasValue)
        {
            oemId = (ushort)(productId.Value & ushort.MaxValue);
        }

        return new()
        {
            BinaryVersion = ReadUInt32(payload, 0x00),
            TmeFirmwareQtiVersion = ReadUInt32(payload, 0x04),
            TmeFirmwareOemVersion = ReadUInt32(payload, 0x08),
            XblSecureCoreQtiVersion = ReadUInt32(payload, 0x0C),
            XblSecureCoreOemVersion = ReadUInt32(payload, 0x10),
            XblSecureCoreExtendedOemVersion = ReadUInt32(payload, 0x14),
            DeviceProgrammerOemVersion = ReadUInt32(payload, 0x18),
            XblConfigOemVersion = ReadUInt32(payload, 0x1C),
            SocHardwareVersion = ReadUInt32(payload, 0x20),
            JtagId = ReadUInt32(payload, 0x24),
            RawOemId = rawOemId,
            ProductId = productId,
            OemLifeCycleState = ReadOptionalUInt32(payload, 0x30),
            MrcActivationList = ReadOptionalUInt32(payload, 0x34),
            MrcRevocationList = ReadOptionalUInt32(payload, 0x38),
            NumberOfRootCertificates = ReadOptionalUInt32(payload, 0x3C),
            AppsSecureDebugStatus = ReadOptionalUInt32(payload, 0x40),
            PublicKeyHashInFuse = ReadOptionalUInt32(payload, 0x44),
            OemAuthenticationEnabled = ReadOptionalUInt32(payload, 0x48),
            RomPublicKeyHashIndex = ReadOptionalUInt32(payload, 0x4C),
            OemId = oemId,
            ModelId = modelId
        };
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> payload, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(offset, sizeof(uint)));
    }

    private static uint? ReadOptionalUInt32(ReadOnlySpan<byte> payload, int offset)
    {
        return payload.Length >= offset + sizeof(uint) ? ReadUInt32(payload, offset) : null;
    }
}
