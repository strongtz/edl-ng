using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using QCEDL.NET.Logging;
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
        if (TryParseTextualReport(payload, out var textualChipInfo))
        {
            return textualChipInfo;
        }

        if (payload.Length < MinimumHwidPayloadLength)
        {
            throw new BadMessageException(
                $"Sahara v3 CMD10 returned {payload.Length} bytes; at least {MinimumHwidPayloadLength} bytes are required to reconstruct the HWID.");
        }

        if (payload.Length % sizeof(uint) != 0)
        {
            var alignedLength = payload.Length - (payload.Length % sizeof(uint));
            LibraryLogger.Debug(
                $"Sahara v3 CMD10 payload is {payload.Length} bytes; parsing the first {alignedLength}.");
            payload = payload[..alignedLength];
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

        var chipInfo = new QualcommSaharaV3ChipInfo
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

        chipInfo.LogReport();
        return chipInfo;
    }

    private void LogReport()
    {
        LibraryLogger.Info($"CMD REV INFO: 0x{BinaryVersion:X}");
        LibraryLogger.Info($"SOC_HW_VERSION: 0x{SocHardwareVersion:X}");
        LibraryLogger.Info($"JTAG_ID: 0x{JtagId:X}");
        LibraryLogger.Info($"OEM_ID: 0x{RawOemId:X}");
        LogOptional("OEM_PRODUCT_ID", ProductId);
        LogOptional("OEM_LCS", OemLifeCycleState);
        LogOptional("OEM MRC", MrcActivationList);
        LogOptional("OEM MRC REVOK", MrcRevocationList);
        LogOptional("CERTS", NumberOfRootCertificates);
        LogOptional("DBG", AppsSecureDebugStatus);
        LogOptional("AUTH", OemAuthenticationEnabled);
        LogOptional("HASH FUSE", PublicKeyHashInFuse);
        LogOptional("ROM IDX", RomPublicKeyHashIndex);
    }

    private static void LogOptional(string name, uint? value)
    {
        if (value.HasValue)
        {
            LibraryLogger.Info($"{name}: 0x{value.Value:X}");
        }
    }

    private static bool TryParseTextualReport(ReadOnlySpan<byte> payload, out QualcommSaharaV3ChipInfo chipInfo)
    {
        chipInfo = null!;
        if (payload.IndexOf("SOC_HW_VERSION"u8) < 0)
        {
            return false;
        }

        var report = Encoding.ASCII.GetString(payload);
        var fields = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in report.Split('\n'))
        {
            var line = rawLine.Trim('\r', ' ', '\0');
            if (line.Length == 0)
            {
                continue;
            }

            LibraryLogger.Info(line);
            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (uint.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                        out var parsed))
                {
                    fields[key] = parsed;
                }
            }
            else if (uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var decimalValue))
            {
                fields[key] = decimalValue;
            }
        }

        uint Field(string key)
        {
            return fields.TryGetValue(key, out var value) ? value : 0;
        }

        uint? OptionalField(string key)
        {
            return fields.TryGetValue(key, out var value) ? value : null;
        }

        var rawOemId = Field("OEM_ID");
        var productId = OptionalField("OEM_PRODUCT_ID");
        var oemId = (ushort)(rawOemId & ushort.MaxValue);
        var modelId = (ushort)(rawOemId >> 16);
        if (oemId == 0 && productId is > 0)
        {
            oemId = (ushort)(productId.Value & ushort.MaxValue);
        }

        chipInfo = new()
        {
            BinaryVersion = Field("CMD REV INFO"),
            TmeFirmwareQtiVersion = Field("FW QC_ARB"),
            TmeFirmwareOemVersion = Field("FW OEM_ARB"),
            XblSecureCoreQtiVersion = Field("XBL QC_ARB"),
            XblSecureCoreOemVersion = Field("XBL OEM_ARB"),
            XblSecureCoreExtendedOemVersion = Field("BB ARB"),
            DeviceProgrammerOemVersion = Field("QTI_MISC ARB"),
            XblConfigOemVersion = Field("CFG OEM_ARB"),
            SocHardwareVersion = Field("SOC_HW_VERSION"),
            JtagId = Field("JTAG_ID"),
            RawOemId = rawOemId,
            ProductId = productId,
            OemLifeCycleState = OptionalField("OEM_LCS"),
            MrcActivationList = OptionalField("OEM MRC"),
            MrcRevocationList = OptionalField("OEM MRC REVOK"),
            NumberOfRootCertificates = OptionalField("CERTS"),
            AppsSecureDebugStatus = OptionalField("DBG"),
            PublicKeyHashInFuse = OptionalField("HASH FUSE"),
            OemAuthenticationEnabled = OptionalField("AUTH"),
            RomPublicKeyHashIndex = OptionalField("ROM IDX"),
            OemId = oemId,
            ModelId = modelId
        };

        return true;
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
