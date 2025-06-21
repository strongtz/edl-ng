using System.Xml.Serialization;

namespace Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

// IOData, DevData
public class IoDataDevDataMixin : DevData
{
    private uint? _sectorSizeInBytes;

    [XmlAttribute(AttributeName = "SECTOR_SIZE_IN_BYTES")]
    public uint SectorSizeInBytes
    {
        get => _sectorSizeInBytes ?? 0; set => _sectorSizeInBytes = value;
    }

    public bool ShouldSerializeSectorSizeInBytes()
    {
        return _sectorSizeInBytes.HasValue;
    }

    private string? _numPartitionSectors;

    [XmlAttribute(AttributeName = "num_partition_sectors")]
    public string NumPartitionSectors
    {
        get => _numPartitionSectors ?? ""; set => _numPartitionSectors = value;
    }

    public bool ShouldSerializeNumPartitionSectors()
    {
        return _numPartitionSectors != null;
    }

    private string? _startSector;

    [XmlAttribute(AttributeName = "start_sector")]
    public string StartSector
    {
        get => _startSector ?? ""; set => _startSector = value;
    }

    public bool ShouldSerializeStartSector()
    {
        return _startSector != null;
    }
}