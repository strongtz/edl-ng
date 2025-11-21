namespace QCEDL.CLI.Core;

/// <summary>
/// Represents basic storage geometry information exposed by different backends.
/// </summary>
internal readonly record struct StorageGeometry(uint SectorSize, ulong? TotalSectors);