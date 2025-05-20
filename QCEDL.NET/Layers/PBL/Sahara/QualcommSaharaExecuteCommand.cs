namespace Qualcomm.EmergencyDownload.Layers.PBL.Sahara
{
    internal enum QualcommSaharaExecuteCommand : uint
    {
        NOP = 0x00,
        SerialNumRead = 0x01,
        MsmHWIDRead = 0x02,
        OemPKHashRead = 0x03,
        SwitchDMSS = 0x04,
        SwitchStreaming = 0x05,
        ReadDebugData = 0x06,
        GetSoftwareVersionSBL = 0x07
    }
}
