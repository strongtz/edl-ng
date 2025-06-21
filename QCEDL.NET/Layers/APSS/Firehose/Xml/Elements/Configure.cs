using System.Xml.Serialization;

namespace Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

public class Configure
{
    private StorageType? _memoryName;

    [XmlAttribute(AttributeName = "MemoryName")]
    public StorageType MemoryName
    {
        get => _memoryName ?? StorageType.Ufs; set => _memoryName = value;
    }

    public bool ShouldSerializeMemoryName()
    {
        return _memoryName.HasValue;
    }

    private string? _verbose;

    [XmlAttribute(AttributeName = "Verbose")]
    public string Verbose
    {
        get => _verbose ?? "0"; set => _verbose = value;
    }

    public bool ShouldSerializeVerbose()
    {
        return _verbose != null;
    }

    private ulong? _maxPayloadSizeToTargetInBytes;

    [XmlAttribute(AttributeName = "MaxPayloadSizeToTargetInBytes")]
    public ulong MaxPayloadSizeToTargetInBytes
    {
        get => _maxPayloadSizeToTargetInBytes ?? 0; set => _maxPayloadSizeToTargetInBytes = value;
    }

    public bool ShouldSerializeMaxPayloadSizeToTargetInBytes()
    {
        return _maxPayloadSizeToTargetInBytes.HasValue;
    }

    private string? _alwaysValidate;

    [XmlAttribute(AttributeName = "AlwaysValidate")]
    public string AlwaysValidate
    {
        get => _alwaysValidate ?? "0"; set => _alwaysValidate = value;
    }

    public bool ShouldSerializeAlwaysValidate()
    {
        return _alwaysValidate != null;
    }

    private ulong? _maxDigestTableSizeInBytes;

    [XmlAttribute(AttributeName = "MaxDigestTableSizeInBytes")]
    public ulong MaxDigestTableSizeInBytes
    {
        get => _maxDigestTableSizeInBytes ?? 0; set => _maxDigestTableSizeInBytes = value;
    }

    public bool ShouldSerializeMaxDigestTableSizeInBytes()
    {
        return _maxDigestTableSizeInBytes.HasValue;
    }

    private string? _zlpAwareHost;

    [XmlAttribute(AttributeName = "ZlpAwareHost")]
    public string ZlpAwareHost
    {
        get => _zlpAwareHost ?? "1"; set => _zlpAwareHost = value;
    }

    public bool ShouldSerializeZlpAwareHost()
    {
        return _zlpAwareHost != null;
    }

    private string? _skipWrite;

    [XmlAttribute(AttributeName = "SkipWrite")]
    public string SkipWrite
    {
        get => _skipWrite ?? "0"; set => _skipWrite = value;
    }

    public bool ShouldSerializeSkipWrite()
    {
        return _skipWrite != null;
    }

    private string? _skipStorageInit;

    [XmlAttribute(AttributeName = "SkipStorageInit")]
    public string SkipStorageInit
    {
        get => _skipStorageInit ?? "0"; set => _skipStorageInit = value;
    }

    public bool ShouldSerializeSkipStorageInit()
    {
        return _skipStorageInit != null;
    }
}