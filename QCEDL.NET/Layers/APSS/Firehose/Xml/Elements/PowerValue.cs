using System.Xml.Serialization;

namespace Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

public enum PowerValue
{
    [XmlEnum(Name = "reset")]
    Reset,
    [XmlEnum(Name = "off")]
    Off,
    [XmlEnum(Name = "reset_to_edl")]
    Edl
}