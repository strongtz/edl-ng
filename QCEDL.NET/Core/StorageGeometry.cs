namespace Qualcomm.EmergencyDownload.Core;

/// <summary>
/// Represents basic storage geometry information exposed by different backends.
/// </summary>
public readonly record struct StorageGeometry(uint SectorSize, ulong? TotalSectors);