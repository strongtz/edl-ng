namespace Qualcomm.EmergencyDownload.Layers.PBL.Sahara;

public sealed record QualcommSaharaRamDumpRegion(
    ulong Type,
    ulong Address,
    ulong Length,
    string RegionName,
    string FileName);
