using System.Xml;
using System.Xml.Serialization;

namespace Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements
{
    public class IOData
    {
        private StorageType? storageType;

        [XmlAttribute(AttributeName = "storage_type")]
        public StorageType StorageType
        {
            get => storageType ?? StorageType.UFS; set => storageType = value;
        }

        public bool ShouldSerializeStorageType()
        {
            return storageType.HasValue;
        }

        private uint? slot;

        [XmlAttribute(AttributeName = "slot")]
        public uint Slot
        {
            get => slot ?? 0; set => slot = value;
        }

        public bool ShouldSerializeSlot()
        {
            return slot.HasValue;
        }

        private uint? physicalPartitionNumber;

        [XmlAttribute(AttributeName = "physical_partition_number")]
        public uint PhysicalPartitionNumber
        {
            get => physicalPartitionNumber ?? 0; set => physicalPartitionNumber = value;
        }

        public bool ShouldSerializePhysicalPartitionNumber()
        {
            return physicalPartitionNumber.HasValue;
        }
    }
}
