using System.Xml;
using System.Xml.Serialization;

namespace Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements
{
    public class GetStorageInfo : DevData
    {
        private ulong? printJson;

        [XmlAttribute(AttributeName = "print_json")]
        public ulong PrintJson
        {
            get => printJson ?? 1; set => printJson = value;
        }

        public bool ShouldSerializePrintJson()
        {
            return printJson.HasValue;
        }
    }
}
