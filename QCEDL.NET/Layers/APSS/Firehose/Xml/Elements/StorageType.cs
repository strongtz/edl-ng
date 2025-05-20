using System.Xml.Serialization;

namespace Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements
{
    public enum StorageType
    {
        [XmlEnum(Name = "eMMC")]
        SDCC,
        [XmlEnum(Name = "spinor")]
        SPINOR,
        [XmlEnum(Name = "UFS")]
        UFS,
        [XmlEnum(Name = "nand")]
        NAND,
        [XmlEnum(Name = "NVMe")]
        NVME
    }
}
