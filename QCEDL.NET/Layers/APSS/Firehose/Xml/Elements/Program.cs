using System.Xml.Serialization;

namespace Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

public class Program : IODataDevDataMixin
{
    private string? filename;

    [XmlAttribute(AttributeName = "filename")]
    public string FileName
    {
        get => filename ?? ""; set => filename = value;
    }

    public bool ShouldSerializeFileName()
    {
        return filename != null;
    }
}