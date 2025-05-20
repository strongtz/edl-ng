using System.Xml.Serialization;

namespace Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements
{
    public enum PowerValue
    {
        [XmlEnum(Name = "reset")]
        reset,
        [XmlEnum(Name = "off")]
        off,
        [XmlEnum(Name = "reset_to_edl")]
        edl
    }
}
