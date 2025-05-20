using System.Xml;
using System.Xml.Serialization;

namespace Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements
{
    public class Data
    {
        [XmlElement(ElementName = "power")]
        public Power? Power
        {
            get; set;
        }

        [XmlElement(ElementName = "log")]
        public Log? Log
        {
            get; set;
        }

        [XmlElement(ElementName = "response")]
        public Response? Response
        {
            get; set;
        }

        [XmlElement(ElementName = "getstorageinfo")]
        public GetStorageInfo? GetStorageInfo
        {
            get; set;
        }

        [XmlElement(ElementName = "read")]
        public Read? Read
        {
            get; set;
        }

        [XmlElement(ElementName = "configure")]
        public Configure? Configure
        {
            get; set;
        }

        [XmlElement(ElementName = "nop")]
        public Nop? Nop
        {
            get; set;
        }

        [XmlElement(ElementName = "program")]
        public Program? Program
        {
            get; set;
        }
    }
}
