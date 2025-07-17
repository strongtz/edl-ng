using System.Globalization;
using QCEDL.NET.Logging;

namespace Qualcomm.EmergencyDownload.ChipInfo;

public class HardwareId
{
    // Also known as JTAGID
    internal static void ParseMsmid(uint msmid)
    {
        var manufacturerId = GetManufacturerIdFromMsmid(msmid);
        var productId = GetProductIdFromMsmid(msmid);
        var dieRevision = GetDieRevisionFromMsmid(msmid);

        if (manufacturerId == 0x0E1)
        {
            LibraryLogger.Debug($"Manufacturer ID: {manufacturerId:X3} (Qualcomm)");
        }
        else
        {
            LibraryLogger.Debug($"Manufacturer ID: {manufacturerId:X3} (Unknown)");
        }

        if (Enum.IsDefined(typeof(QualcommPartNumbers), productId))
        {
            var partNumber = (QualcommPartNumbers)productId;
            LibraryLogger.Info($"Product ID: {productId} ({partNumber.ToStringSnakeCaseUpper()})");
        }
        else
        {
            LibraryLogger.Info($"Product ID: {productId} (Unknown)");
        }

        LibraryLogger.Debug($"Die Revision: {dieRevision:X1}");
    }

    internal static uint GetManufacturerIdFromMsmid(uint msmid) => msmid & 0xFFF;

    internal static uint GetProductIdFromMsmid(uint msmid) => (msmid >> 12) & 0xFFFF;

    internal static uint GetDieRevisionFromMsmid(uint msmid) => (msmid >> 28) & 0xF;

    internal static uint GetMsmidFromHwid(byte[] hwid)
    {
        var hwidStr = Convert.ToHexString(hwid);
        return uint.Parse(hwidStr.AsSpan(hwidStr.Length - 16, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    internal static uint GetOemidFromHwid(byte[] hwid)
    {
        var hwidStr = Convert.ToHexString(hwid);
        return uint.Parse(hwidStr.AsSpan(hwidStr.Length - 8, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    internal static uint GetModelIdFromHwid(byte[] hwid)
    {
        var hwidStr = Convert.ToHexString(hwid);
        return uint.Parse(hwidStr.AsSpan(hwidStr.Length - 4, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    public static void ParseHwid(byte[] hwid)
    {
        var msmid = GetMsmidFromHwid(hwid);
        var oemid = GetOemidFromHwid(hwid);
        var modelId = GetModelIdFromHwid(hwid);

        ParseMsmid(msmid);
        LibraryLogger.Debug($"OEM: {oemid:X4}");
        LibraryLogger.Debug($"Model: {modelId:X4}");
    }
}