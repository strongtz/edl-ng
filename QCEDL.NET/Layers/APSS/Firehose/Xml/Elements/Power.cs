using System.Xml.Serialization;

namespace Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

public class Power
{
    private PowerValue? _value;

    [XmlAttribute(AttributeName = "value")]
    public PowerValue Value
    {
        get => _value ?? PowerValue.Reset; set => _value = value;
    }

    public bool ShouldSerializeValue() => _value.HasValue;

    private ulong? _delayInSeconds;

    [XmlAttribute(AttributeName = "DelayInSeconds")]
    public ulong DelayInSeconds
    {
        get => _delayInSeconds ?? 100; set => _delayInSeconds = value;
    }

    public bool ShouldSerializeDelayInSeconds() => _delayInSeconds.HasValue;
}