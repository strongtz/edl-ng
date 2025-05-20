using System.Xml;
using System.Xml.Serialization;

namespace Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements
{
    public class Patches
    {
        [XmlElement(ElementName = "patch")]
        public Patch[] Patch
        {
            get; set;
        }
    }
}
