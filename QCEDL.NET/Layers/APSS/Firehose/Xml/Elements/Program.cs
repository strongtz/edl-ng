using System.Xml.Serialization;

namespace Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

public class Program : IoDataDevDataMixin
{
    private string? _filename;

    [XmlAttribute(AttributeName = "filename")]
    public string FileName
    {
        get => _filename ?? ""; set => _filename = value;
    }

    public bool ShouldSerializeFileName()
    {
        return _filename != null;
    }
}