using System.Xml.Serialization;

namespace Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

public class Power
{
    private PowerValue? value;

    [XmlAttribute(AttributeName = "value")]
    public PowerValue Value
    {
        get => value ?? PowerValue.reset; set => this.value = value;
    }

    public bool ShouldSerializeValue()
    {
        return value.HasValue;
    }

    private ulong? delayInSeconds;

    [XmlAttribute(AttributeName = "DelayInSeconds")]
    public ulong DelayInSeconds
    {
        get => delayInSeconds ?? 100; set => delayInSeconds = value;
    }

    public bool ShouldSerializeDelayInSeconds()
    {
        return delayInSeconds.HasValue;
    }
}