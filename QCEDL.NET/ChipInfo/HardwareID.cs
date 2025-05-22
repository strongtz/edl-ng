using System.Globalization;
using QCEDL.NET.Logging;

namespace Qualcomm.EmergencyDownload.ChipInfo;

public class HardwareID
{
    // Also known as JTAGID
    internal static void ParseMSMID(uint MSMID)
    {
        var ManufacturerID = GetManufacturerIDFromMSMID(MSMID);
        var ProductID = GetProductIDFromMSMID(MSMID);
        var DieRevision = GetDieRevisionFromMSMID(MSMID);

        if (ManufacturerID == 0x0E1)
        {
            LibraryLogger.Debug($"Manufacturer ID: {ManufacturerID:X3} (Qualcomm)");
        }
        else
        {
            LibraryLogger.Debug($"Manufacturer ID: {ManufacturerID:X3} (Unknown)");
        }

        if (Enum.IsDefined(typeof(QualcommPartNumbers), ProductID))
        {
            LibraryLogger.Info($"Product ID: {ProductID} ({(QualcommPartNumbers)ProductID})");
        }
        else
        {
            LibraryLogger.Info($"Product ID: {ProductID} (Unknown)");
        }

        LibraryLogger.Debug($"Die Revision: {DieRevision:X1}");
    }

    internal static uint GetManufacturerIDFromMSMID(uint MSMID)
    {
        return MSMID & 0xFFF;
    }

    internal static uint GetProductIDFromMSMID(uint MSMID)
    {
        return (MSMID >> 12) & 0xFFFF;
    }

    internal static uint GetDieRevisionFromMSMID(uint MSMID)
    {
        return (MSMID >> 28) & 0xF;
    }

    internal static uint GetMSMIDFromHWID(byte[] HWID)
    {
        var HWIDStr = Convert.ToHexString(HWID);
        return uint.Parse(HWIDStr.AsSpan(HWIDStr.Length - 16, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    internal static uint GetOEMIDFromHWID(byte[] HWID)
    {
        var HWIDStr = Convert.ToHexString(HWID);
        return uint.Parse(HWIDStr.AsSpan(HWIDStr.Length - 8, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    internal static uint GetModelIDFromHWID(byte[] HWID)
    {
        var HWIDStr = Convert.ToHexString(HWID);
        return uint.Parse(HWIDStr.AsSpan(HWIDStr.Length - 4, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    public static void ParseHWID(byte[] HWID)
    {
        var MSMID = GetMSMIDFromHWID(HWID);
        var OEMID = GetOEMIDFromHWID(HWID);
        var ModelID = GetModelIDFromHWID(HWID);

        ParseMSMID(MSMID);
        LibraryLogger.Debug($"OEM: {OEMID:X4}");
        LibraryLogger.Debug($"Model: {ModelID:X4}");
    }
}