﻿using System.Runtime.InteropServices;

namespace QCEDL.NET.PartitionTable;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct GPTHeader
{
    public ulong Signature;
    public uint Revision;
    public uint Size;
    public uint CRC32;
    public uint Reserved;
    public ulong CurrentLBA;
    public ulong BackupLBA;
    public ulong FirstUsableLBA;
    public ulong LastUsableLBA;
    public Guid DiskGUID;
    public ulong PartitionArrayLBA;
    public uint PartitionEntryCount;
    public uint PartitionEntrySize;
    public uint PartitionArrayCRC32;
}