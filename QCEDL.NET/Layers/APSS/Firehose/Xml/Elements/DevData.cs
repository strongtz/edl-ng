using System.Xml.Serialization;

namespace Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

public class DevData
{
    private StorageType? _storageType;

    [XmlAttribute(AttributeName = "storage_type")]
    public StorageType StorageType
    {
        get => _storageType ?? StorageType.Ufs; set => _storageType = value;
    }

    public bool ShouldSerializeStorageType()
    {
        return _storageType.HasValue;
    }

    private uint? _slot;

    [XmlAttribute(AttributeName = "slot")]
    public uint Slot
    {
        get => _slot ?? 0; set => _slot = value;
    }

    public bool ShouldSerializeSlot()
    {
        return _slot.HasValue;
    }

    private uint? _physicalPartitionNumber;

    [XmlAttribute(AttributeName = "physical_partition_number")]
    public uint PhysicalPartitionNumber
    {
        get => _physicalPartitionNumber ?? 0; set => _physicalPartitionNumber = value;
    }

    public bool ShouldSerializePhysicalPartitionNumber()
    {
        return _physicalPartitionNumber.HasValue;
    }
}