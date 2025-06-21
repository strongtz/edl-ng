using System.Xml.Serialization;

namespace Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

// IOOptions, IOData, DevData
public class IoOptionsIoDataDevDataMixin : IoDataDevDataMixin
{
    private ulong? _lastSector;

    [XmlAttribute(AttributeName = "last_sector")]
    public ulong LastSector
    {
        get => _lastSector ?? 0; set => _lastSector = value;
    }

    public bool ShouldSerializeLastSector()
    {
        return _lastSector.HasValue;
    }

    private byte? _skipBadBlock;

    [XmlAttribute(AttributeName = "skip_bad_block")]
    public byte SkipBadBlock
    {
        get => _skipBadBlock ?? 0; set => _skipBadBlock = value;
    }

    public bool ShouldSerializeSkipBadBlock()
    {
        return _skipBadBlock.HasValue;
    }

    private byte? _getSpare;

    [XmlAttribute(AttributeName = "get_spare")]
    public byte GetSpare
    {
        get => _getSpare ?? 0; set => _getSpare = value;
    }

    public bool ShouldSerializeGetSpare()
    {
        return _getSpare.HasValue;
    }

    private byte? _eccDisabled;

    [XmlAttribute(AttributeName = "ecc_disabled")]
    public byte EccDisabled
    {
        get => _eccDisabled ?? 0; set => _eccDisabled = value;
    }

    public bool ShouldSerializeEccDisabled()
    {
        return _eccDisabled.HasValue;
    }
}