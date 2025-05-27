// Copyright (c) 2018, Rene Lergner - @Heathcliff74xda
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

// These functions assume same endianness for the CPU architecture and the raw data it reads from or writes to.

using System.Collections;
using System.Text;

namespace Qualcomm.EmergencyDownload.Layers.PBL.Sahara;

internal static class ByteOperations
{
    internal static string ReadAsciiString(byte[] ByteArray, uint Offset, uint Length)
    {
        var Bytes = new byte[Length];
        Buffer.BlockCopy(ByteArray, (int)Offset, Bytes, 0, (int)Length);
        return Encoding.ASCII.GetString(Bytes);
    }

    internal static string ReadUnicodeString(byte[] ByteArray, uint Offset, uint Length)
    {
        var Bytes = new byte[Length];
        Buffer.BlockCopy(ByteArray, (int)Offset, Bytes, 0, (int)Length);
        return Encoding.Unicode.GetString(Bytes);
    }

    internal static void WriteAsciiString(byte[] ByteArray, uint Offset, string Text, uint? MaxBufferLength = null)
    {
        if (MaxBufferLength != null)
        {
            Array.Clear(ByteArray, (int)Offset, (int)MaxBufferLength);
        }

        var TextBytes = Encoding.ASCII.GetBytes(Text);
        var WriteLength = TextBytes.Length;
        if (WriteLength > MaxBufferLength)
        {
            WriteLength = (int)MaxBufferLength;
        }

        Buffer.BlockCopy(TextBytes, 0, ByteArray, (int)Offset, WriteLength);
    }

    internal static void WriteUnicodeString(byte[] ByteArray, uint Offset, string Text, uint? MaxBufferLength = null)
    {
        if (MaxBufferLength != null)
        {
            Array.Clear(ByteArray, (int)Offset, (int)MaxBufferLength);
        }

        var TextBytes = Encoding.Unicode.GetBytes(Text);
        var WriteLength = TextBytes.Length;
        if (WriteLength > MaxBufferLength)
        {
            WriteLength = (int)MaxBufferLength;
        }

        Buffer.BlockCopy(TextBytes, 0, ByteArray, (int)Offset, WriteLength);
    }

    internal static uint ReadUInt32(byte[] ByteArray, uint Offset)
    {
        return BitConverter.ToUInt32(ByteArray, (int)Offset);
    }

    internal static void WriteUInt32(byte[] ByteArray, uint Offset, uint Value)
    {
        Buffer.BlockCopy(BitConverter.GetBytes(Value), 0, ByteArray, (int)Offset, 4);
    }

    internal static int ReadInt32(byte[] ByteArray, uint Offset)
    {
        return BitConverter.ToInt32(ByteArray, (int)Offset);
    }

    internal static void WriteInt32(byte[] ByteArray, uint Offset, int Value)
    {
        Buffer.BlockCopy(BitConverter.GetBytes(Value), 0, ByteArray, (int)Offset, 4);
    }

    internal static ushort ReadUInt16(byte[] ByteArray, uint Offset)
    {
        return BitConverter.ToUInt16(ByteArray, (int)Offset);
    }

    internal static void WriteUInt16(byte[] ByteArray, uint Offset, ushort Value)
    {
        Buffer.BlockCopy(BitConverter.GetBytes(Value), 0, ByteArray, (int)Offset, 2);
    }

    internal static short ReadInt16(byte[] ByteArray, uint Offset)
    {
        return BitConverter.ToInt16(ByteArray, (int)Offset);
    }

    internal static void WriteInt16(byte[] ByteArray, uint Offset, short Value)
    {
        Buffer.BlockCopy(BitConverter.GetBytes(Value), 0, ByteArray, (int)Offset, 2);
    }

    internal static byte ReadUInt8(byte[] ByteArray, uint Offset)
    {
        return ByteArray[Offset];
    }

    internal static void WriteUInt8(byte[] ByteArray, uint Offset, byte Value)
    {
        ByteArray[Offset] = Value;
    }

    internal static uint ReadUInt24(byte[] ByteArray, uint Offset)
    {
        return (uint)(ByteArray[Offset] + (ByteArray[Offset + 1] << 8) + (ByteArray[Offset + 2] << 16));
    }

    internal static void WriteUInt24(byte[] ByteArray, uint Offset, uint Value)
    {
        Buffer.BlockCopy(BitConverter.GetBytes(Value), 0, ByteArray, (int)Offset, 3);
    }

    internal static ulong ReadUInt64(byte[] ByteArray, uint Offset)
    {
        return BitConverter.ToUInt64(ByteArray, (int)Offset);
    }

    internal static void WriteUInt64(byte[] ByteArray, uint Offset, ulong Value)
    {
        Buffer.BlockCopy(BitConverter.GetBytes(Value), 0, ByteArray, (int)Offset, 8);
    }

    internal static Guid ReadGuid(byte[] ByteArray, uint Offset)
    {
        var GuidBuffer = new byte[0x10];
        Buffer.BlockCopy(ByteArray, (int)Offset, GuidBuffer, 0, 0x10);
        return new(GuidBuffer);
    }

    internal static void WriteGuid(byte[] ByteArray, uint Offset, Guid Value)
    {
        Buffer.BlockCopy(Value.ToByteArray(), 0, ByteArray, (int)Offset, 0x10);
    }

    internal static uint Align(uint Base, uint Offset, uint Alignment)
    {
        return (Offset - Base) % Alignment == 0 ? Offset : ((Offset - Base) / Alignment + 1) * Alignment + Base;
    }

    internal static uint? FindAscii(byte[] SourceBuffer, string Pattern)
    {
        return FindPattern(SourceBuffer, Encoding.ASCII.GetBytes(Pattern), null, null);
    }

    internal static uint? FindUnicode(byte[] SourceBuffer, string Pattern)
    {
        return FindPattern(SourceBuffer, Encoding.Unicode.GetBytes(Pattern), null, null);
    }

    internal static uint? FindUint(byte[] SourceBuffer, uint Pattern)
    {
        return FindPattern(SourceBuffer, BitConverter.GetBytes(Pattern), null, null);
    }

    internal static uint? FindPattern(byte[] SourceBuffer, byte[] Pattern, byte[]? Mask, byte[]? OutPattern)
    {
        return FindPattern(SourceBuffer, 0, null, Pattern, Mask, OutPattern);
    }

    internal static bool Compare(byte[] Array1, byte[] Array2)
    {
        return StructuralComparisons.StructuralEqualityComparer.Equals(Array1, Array2);
    }

    internal static uint? FindPattern(byte[] SourceBuffer, uint SourceOffset, uint? SourceSize, byte[] Pattern, byte[]? Mask, byte[]? OutPattern)
    {
        // The mask is optional.
        // In the mask 0x00 means the value must match, and 0xFF means that this position is a wildcard.

        uint? Result = null;

        var SearchPosition = SourceOffset;
        int i;

        while (SearchPosition <= SourceBuffer.Length - Pattern.Length && (SourceSize == null || SearchPosition <= SourceOffset + SourceSize - Pattern.Length))
        {
            var Match = true;
            for (i = 0; i < Pattern.Length; i++)
            {
                if (SourceBuffer[SearchPosition + i] != Pattern[i])
                {
                    if (Mask == null || Mask[i] == 0)
                    {
                        Match = false;
                        break;
                    }
                }
            }

            if (Match)
            {
                Result = SearchPosition;

                if (OutPattern != null)
                {
                    Buffer.BlockCopy(SourceBuffer, (int)SearchPosition, OutPattern, 0, Pattern.Length);
                }

                break;
            }

            SearchPosition++;
        }

        return Result;
    }

    internal static byte CalculateChecksum8(byte[] Buffer, uint Offset, uint Size)
    {
        byte Checksum = 0;

        for (var i = Offset; i < Offset + Size; i++)
        {
            Checksum += Buffer[i];
        }

        return (byte)(0x100 - Checksum);
    }

    internal static ushort CalculateChecksum16(byte[] Buffer, uint Offset, uint Size)
    {
        ushort Checksum = 0;

        for (var i = Offset; i < Offset + Size - 1; i += 2)
        {
            Checksum += BitConverter.ToUInt16(Buffer, (int)i);
        }

        return (ushort)(0x10000 - Checksum);
    }
}