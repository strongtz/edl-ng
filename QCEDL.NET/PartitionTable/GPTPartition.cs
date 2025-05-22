using System.Runtime.InteropServices;

namespace QCEDL.NET.PartitionTable;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
public unsafe struct GPTPartition
{
    public Guid TypeGUID;
    public Guid UID;
    public ulong FirstLBA;
    public ulong LastLBA;
    public ulong Attributes;
    public fixed char Name[36];

    public string GetName()
    {
        fixed (char* ptr = Name)
        {
            return new(new Span<char>(ptr, 36));
        }
    }
}