using System.Xml.Serialization;

namespace Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

public class GetStorageInfo : DevData
{
    private ulong? _printJson;

    [XmlAttribute(AttributeName = "print_json")]
    public ulong PrintJson
    {
        get => _printJson ?? 1; set => _printJson = value;
    }

    public bool ShouldSerializePrintJson() => _printJson.HasValue;
}