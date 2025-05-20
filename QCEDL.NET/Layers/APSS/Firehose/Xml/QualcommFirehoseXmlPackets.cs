using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml
{
    internal class QualcommFirehoseXmlPackets
    {
        public static Data GetConfigurePacket(StorageType memoryName, bool verbose, ulong maxPayloadSizeToTargetInBytes, bool alwaysValidate,
                                             ulong maxDigestTableSizeInBytes, bool zlpAwareHost, bool skipWrite, bool skipStorageInit = false)
        {
            return new Data()
            {
                Configure = new Configure()
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

        public static Data GetReadPacket(StorageType storageType, uint LUNi, uint sectorSize, uint FirstSector, uint LastSector)
        {
            return new Data()
            {
                Read = new Read()
                {
                    PhysicalPartitionNumber = LUNi,
                    StorageType = storageType,
                    Slot = 0,
                    SectorSizeInBytes = sectorSize,
                    StartSector = FirstSector.ToString(),
                    LastSector = LastSector,
                    NumPartitionSectors = (LastSector - FirstSector + 1).ToString()
                }
            };
        }

        public static Data GetProgramPacket(StorageType storageType, uint LUNi, uint sectorSize, uint startSector, uint numSectors, string? filename)
        {
            return new Data()
            {
                Program = new Elements.Program()
                {
                    PhysicalPartitionNumber = LUNi,
                    StorageType = storageType,
                    Slot = 0,
                    SectorSizeInBytes = sectorSize,
                    StartSector = startSector.ToString(),
                    NumPartitionSectors = numSectors.ToString(),
                    FileName = filename ?? "dummy.bin"
                }
            };
        }

        public static Data GetPowerPacket(PowerValue powerValue = PowerValue.reset, uint delayInSeconds = 1)
        {
            return new Data()
            {
                Power = new Power()
                {
                    Value = powerValue,
                    DelayInSeconds = delayInSeconds
                }
            };
        }

        public static Data GetStorageInfoPacket(StorageType storageType, uint PhysicalPartitionNumber = 0)
        {
            return new Data()
            {
                GetStorageInfo = new GetStorageInfo()
                {
                    PhysicalPartitionNumber = PhysicalPartitionNumber,
                    StorageType = storageType,
                    //Slot = 0
                }
            };
        }
    }
}
