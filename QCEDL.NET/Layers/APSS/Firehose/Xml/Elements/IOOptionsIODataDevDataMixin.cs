using System.Xml.Serialization;

namespace Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements
{
    // IOOptions, IOData, DevData
    public class IOOptionsIODataDevDataMixin : IODataDevDataMixin
    {
        private ulong? lastSector;

        [XmlAttribute(AttributeName = "last_sector")]
        public ulong LastSector
        {
            get => lastSector ?? 0; set => lastSector = value;
        }

        public bool ShouldSerializeLastSector()
        {
            return lastSector.HasValue;
        }

        private byte? skipBadBlock;

        [XmlAttribute(AttributeName = "skip_bad_block")]
        public byte SkipBadBlock
        {
            get => skipBadBlock ?? 0; set => skipBadBlock = value;
        }

        public bool ShouldSerializeSkipBadBlock()
        {
            return skipBadBlock.HasValue;
        }

        private byte? getSpare;

        [XmlAttribute(AttributeName = "get_spare")]
        public byte GetSpare
        {
            get => getSpare ?? 0; set => getSpare = value;
        }

        public bool ShouldSerializeGetSpare()
        {
            return getSpare.HasValue;
        }

        private byte? eccDisabled;

        [XmlAttribute(AttributeName = "ecc_disabled")]
        public byte ECCDisabled
        {
            get => eccDisabled ?? 0; set => eccDisabled = value;
        }

        public bool ShouldSerializeECCDisabled()
        {
            return eccDisabled.HasValue;
        }
    }
}
