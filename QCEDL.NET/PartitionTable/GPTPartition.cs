using System.Runtime.InteropServices;

namespace QCEDL.NET.PartitionTable
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    public struct GPTPartition
    {
        public readonly void CloneTo(GPTPartition target)
        {
            target.TypeGUID = TypeGUID;
            target.UID = UID;
            target.FirstLBA = FirstLBA;
            target.LastLBA = LastLBA;
            target.Attributes = Attributes;
            target.Name = Name;
        }

        public Guid TypeGUID;
        public Guid UID;
        public ulong FirstLBA;
        public ulong LastLBA;
        public ulong Attributes;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 36)]
        public char[] Name;
    }
}