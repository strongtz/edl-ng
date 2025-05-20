using System.Globalization;
using QCEDL.NET.Logging;

namespace Qualcomm.EmergencyDownload.ChipInfo
{
    public class HardwareID
    {
        // Also known as JTAGID
        internal static void ParseMSMID(uint MSMID)
        {
            uint ManufacturerID = GetManufacturerIDFromMSMID(MSMID);
            uint ProductID = GetProductIDFromMSMID(MSMID);
            uint DieRevision = GetDieRevisionFromMSMID(MSMID);

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
            string HWIDStr = Convert.ToHexString(HWID);
            return uint.Parse(HWIDStr.Substring(HWIDStr.Length - 16, 8), NumberStyles.HexNumber);
        }

        internal static uint GetOEMIDFromHWID(byte[] HWID)
        {
            string HWIDStr = Convert.ToHexString(HWID);
            return uint.Parse(HWIDStr.Substring(HWIDStr.Length - 8, 4), NumberStyles.HexNumber);
        }

        internal static uint GetModelIDFromHWID(byte[] HWID)
        {
            string HWIDStr = Convert.ToHexString(HWID);
            return uint.Parse(HWIDStr.Substring(HWIDStr.Length - 4, 4), NumberStyles.HexNumber);
        }

        public static void ParseHWID(byte[] HWID)
        {
            uint MSMID = GetMSMIDFromHWID(HWID);
            uint OEMID = GetOEMIDFromHWID(HWID);
            uint ModelID = GetModelIDFromHWID(HWID);

            ParseMSMID(MSMID);
            LibraryLogger.Debug($"OEM: {OEMID:X4}");
            LibraryLogger.Debug($"Model: {ModelID:X4}");
        }
    }
}
