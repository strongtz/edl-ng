using System.Xml.Serialization;

namespace Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

public enum StorageType
{
    [XmlEnum(Name = "eMMC")]
    Sdcc,
    [XmlEnum(Name = "spinor")]
    Spinor,
    [XmlEnum(Name = "UFS")]
    Ufs,
    [XmlEnum(Name = "nand")]
    Nand,
    [XmlEnum(Name = "NVMe")]
    Nvme
}