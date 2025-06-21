namespace QCEDL.NET.PartitionTable;

public class Gpt
{
    // "EFI PART" in LE
    private const ulong GptSignatureMagic = 0x5452415020494645ul;

    public required List<GptPartition> Partitions
    {
        get; set;
    }

    public bool IsBackup
    {
        get; set;
    }

    public bool ReflectPartitionEntryCount
    {
        get; set;
    }

    public int SectorSize
    {
        get; set;
    }

    public static unsafe T StructureFromBytes<T>(byte[] arr) where T : unmanaged
    {
        fixed (byte* ptr = arr)
        {
            return *(T*)ptr;
        }
    }

    public static Gpt? ReadFromStream(Stream stream, int sectorSize)
    {
        var array = new byte[sectorSize];
        stream.ReadExactly(array, 0, array.Length);
        var gptheader = StructureFromBytes<GptHeader>(array);

        if (gptheader.Signature != GptSignatureMagic)
        {
            stream.ReadExactly(array, 0, array.Length);
            gptheader = StructureFromBytes<GptHeader>(array);
        }

        if (gptheader.Signature != GptSignatureMagic)
        {
            throw new InvalidDataException("No GPT!");
        }

        Gpt? gpt;

        if (gptheader.Signature == GptSignatureMagic)
        {
            var isBackupGpt = gptheader.CurrentLBA > gptheader.PartitionArrayLBA;
            var reflectPartitionEntryCount = true;

            if (isBackupGpt)
            {
                _ = stream.Seek(-sectorSize, SeekOrigin.Current);
            }

            List<GptPartition> list = [];

            uint num = 0;

            while (num < gptheader.PartitionEntryCount)
            {
                var num2 = sectorSize / (int)gptheader.PartitionEntrySize;
                stream.ReadExactly(array, 0, array.Length);

                for (var i = 0; i < num2; i++)
                {
                    var startOffset = i * (int)gptheader.PartitionEntrySize;
                    var endOffset = (i + 1) * (int)gptheader.PartitionEntrySize;

                    var partitionEntryBuffer = array[startOffset..endOffset];

                    var gptpartition = StructureFromBytes<GptPartition>(partitionEntryBuffer);

                    if (gptpartition.TypeGUID == Guid.Empty)
                    {
                        num = gptheader.PartitionEntryCount;
                        reflectPartitionEntryCount = false;
                        break;
                    }

                    list.Add(gptpartition);
                    num++;
                }
            }

            gpt = new()
            {
                Header = gptheader,
                Partitions = list,
                IsBackup = isBackupGpt,
                ReflectPartitionEntryCount = reflectPartitionEntryCount,
                SectorSize = sectorSize
            };
        }
        else
        {
            gpt = null;
        }

        return gpt;
    }

    public GptHeader Header { get; private set; }
}