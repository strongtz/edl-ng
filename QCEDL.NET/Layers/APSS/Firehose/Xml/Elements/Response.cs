using System.Xml.Serialization;

namespace Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

public class Response
{
    [XmlAttribute(AttributeName = "value")]
    public string? Value { get; set; }

    public bool ShouldSerializeValue() => Value != null;

    [XmlAttribute(AttributeName = "rawmode")]
    public bool RawMode { get; set; }
}