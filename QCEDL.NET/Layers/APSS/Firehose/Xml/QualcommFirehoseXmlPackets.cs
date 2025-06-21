using QCEDL.NET.Extensions;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml;

internal sealed class QualcommFirehoseXmlPackets
{
    public static Data GetConfigurePacket(StorageType memoryName, bool verbose, ulong maxPayloadSizeToTargetInBytes, bool alwaysValidate,
        ulong maxDigestTableSizeInBytes, bool zlpAwareHost, bool skipWrite, bool skipStorageInit = false)
    {
        return new()
        {
            Configure = new()
            {
                MemoryName = memoryName,
                Verbose = verbose ? "1" : "0",
                MaxPayloadSizeToTargetInBytes = maxPayloadSizeToTargetInBytes,
                AlwaysValidate = alwaysValidate ? "1" : "0",
                MaxDigestTableSizeInBytes = maxDigestTableSizeInBytes,
                ZlpAwareHost = zlpAwareHost ? "1" : "0",
                SkipWrite = skipWrite ? "1" : "0",
                SkipStorageInit = skipStorageInit ? "1" : "0"
            }
        };
    }

    public static Data GetReadPacket(StorageType storageType, uint luNi, uint slot, uint sectorSize, uint firstSector, uint lastSector)
    {
        return new()
        {
            Read = new()
            {
                PhysicalPartitionNumber = luNi,
                Slot = slot,
                StorageType = storageType,
                SectorSizeInBytes = sectorSize,
                StartSector = firstSector.ToStringInvariantCulture(),
                LastSector = lastSector,
                NumPartitionSectors = (lastSector - firstSector + 1).ToStringInvariantCulture()
            }
        };
    }

    public static Data GetProgramPacket(StorageType storageType, uint luNi, uint slot, uint sectorSize, uint startSector, uint numSectors, string? filename)
    {
        return new()
        {
            Program = new()
            {
                PhysicalPartitionNumber = luNi,
                StorageType = storageType,
                Slot = slot,
                SectorSizeInBytes = sectorSize,
                StartSector = startSector.ToStringInvariantCulture(),
                NumPartitionSectors = numSectors.ToStringInvariantCulture(),
                FileName = filename ?? "dummy.bin"
            }
        };
    }

    public static Data GetErasePacket(StorageType storageType, uint luNi, uint slot, uint sectorSize, uint startSector, uint numSectorsToErase)
    {
        return new()
        {
            Erase = new()
            {
                PhysicalPartitionNumber = luNi,
                StorageType = storageType,
                Slot = slot,
                SectorSizeInBytes = sectorSize,
                StartSector = startSector.ToStringInvariantCulture(),
                NumPartitionSectors = numSectorsToErase.ToStringInvariantCulture()
            }
        };
    }

    public static Data GetPowerPacket(PowerValue powerValue = PowerValue.Reset, uint delayInSeconds = 1)
    {
        return new()
        {
            Power = new()
            {
                Value = powerValue,
                DelayInSeconds = delayInSeconds
            }
        };
    }

    public static Data GetStorageInfoPacket(StorageType storageType, uint physicalPartitionNumber = 0, uint slot = 0)
    {
        return new()
        {
            GetStorageInfo = new()
            {
                PhysicalPartitionNumber = physicalPartitionNumber,
                StorageType = storageType,
                Slot = slot
            }
        };
    }
}