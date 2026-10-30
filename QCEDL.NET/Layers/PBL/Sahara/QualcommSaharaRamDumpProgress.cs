namespace Qualcomm.EmergencyDownload.Layers.PBL.Sahara;

public sealed record QualcommSaharaRamDumpProgress(
    QualcommSaharaRamDumpRegion Region,
    ulong BytesCompleted,
    ulong TotalBytes);
