using System.Runtime.InteropServices;

namespace QCEDL.NET.PartitionTable
{
    public class GPT
    {
        public required List<GPTPartition> Partitions
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

        public static T StructureFromBytes<T>(byte[] arr) where T : struct
        {
            T str = default;

            GCHandle h = default;

            try
            {
                h = GCHandle.Alloc(arr, GCHandleType.Pinned);

                str = Marshal.PtrToStructure<T>(h.AddrOfPinnedObject());

            }
            finally
            {
                if (h.IsAllocated)
                {
                    h.Free();
                }
            }

            return str;
        }

        public static GPT ReadFromStream(Stream stream, int sectorSize)
        {
            byte[] array = new byte[sectorSize];
            stream.ReadExactly(array, 0, array.Length);
            GPTHeader gptheader = StructureFromBytes<GPTHeader>(array);

            if (new string(gptheader.Signature) != "EFI PART")
            {
                stream.ReadExactly(array, 0, array.Length);
                gptheader = StructureFromBytes<GPTHeader>(array);
            }

            if (new string(gptheader.Signature) != "EFI PART")
            {
                throw new InvalidDataException("No GPT!");
            }

            GPT? gpt;

            if (new string(gptheader.Signature) == "EFI PART")
            {
                bool isBackupGPT = gptheader.CurrentLBA > gptheader.PartitionArrayLBA;
                bool reflectPartitionEntryCount = true;

                if (isBackupGPT)
                {
                    stream.Seek(-sectorSize, SeekOrigin.Current);
                }

                List<GPTPartition> list = [];

                uint num = 0;

                while (num < gptheader.PartitionEntryCount)
                {
                    int num2 = sectorSize / (int)gptheader.PartitionEntrySize;
                    stream.ReadExactly(array, 0, array.Length);

                    for (int i = 0; i < num2; i++)
                    {
                        int startOffset = i * (int)gptheader.PartitionEntrySize;
                        int endOffset = (i + 1) * (int)gptheader.PartitionEntrySize;

                        byte[] partitionEntryBuffer = array[startOffset..endOffset];

                        GPTPartition gptpartition = StructureFromBytes<GPTPartition>(partitionEntryBuffer);

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

                gpt = new GPT
                {
                    Header = gptheader,
                    Partitions = list,
                    IsBackup = isBackupGPT,
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

        public GPTHeader Header;
    }
}