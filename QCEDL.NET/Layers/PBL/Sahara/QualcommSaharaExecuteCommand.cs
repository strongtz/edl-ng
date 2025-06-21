namespace Qualcomm.EmergencyDownload.Layers.PBL.Sahara;

internal enum QualcommSaharaExecuteCommand : uint
{
    Nop = 0x00,
    SerialNumRead = 0x01,
    MsmHwidRead = 0x02,
    OemPkHashRead = 0x03,
    SwitchDmss = 0x04,
    SwitchStreaming = 0x05,
    ReadDebugData = 0x06,
    GetSoftwareVersionSbl = 0x07
}