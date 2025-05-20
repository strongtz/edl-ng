using System.Xml;
using System.Xml.Serialization;

namespace Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements
{
    // IOData, DevData
    public class IODataDevDataMixin : DevData
    {
        private uint? sectorSizeInBytes;

        [XmlAttribute(AttributeName = "SECTOR_SIZE_IN_BYTES")]
        public uint SectorSizeInBytes
        {
            get => sectorSizeInBytes ?? 0; set => sectorSizeInBytes = value;
        }

        public bool ShouldSerializeSectorSizeInBytes()
        {
            return sectorSizeInBytes.HasValue;
        }

        private string? numPartitionSectors;

        [XmlAttribute(AttributeName = "num_partition_sectors")]
        public string NumPartitionSectors
        {
            get => numPartitionSectors ?? ""; set => numPartitionSectors = value;
        }

        public bool ShouldSerializeNumPartitionSectors()
        {
            return numPartitionSectors != null;
        }

        private string? startSector;

        [XmlAttribute(AttributeName = "start_sector")]
        public string StartSector
        {
            get => startSector ?? ""; set => startSector = value;
        }

        public bool ShouldSerializeStartSector()
        {
            return startSector != null;
        }
    }
}
