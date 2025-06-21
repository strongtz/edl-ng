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
    internal static string ReadAsciiString(byte[] byteArray, uint offset, uint length)
    {
        var bytes = new byte[length];
        Buffer.BlockCopy(byteArray, (int)offset, bytes, 0, (int)length);
        return Encoding.ASCII.GetString(bytes);
    }

    internal static string ReadUnicodeString(byte[] byteArray, uint offset, uint length)
    {
        var bytes = new byte[length];
        Buffer.BlockCopy(byteArray, (int)offset, bytes, 0, (int)length);
        return Encoding.Unicode.GetString(bytes);
    }

    internal static void WriteAsciiString(byte[] byteArray, uint offset, string text, uint? maxBufferLength = null)
    {
        if (maxBufferLength != null)
        {
            Array.Clear(byteArray, (int)offset, (int)maxBufferLength);
        }

        var textBytes = Encoding.ASCII.GetBytes(text);
        var writeLength = textBytes.Length;
        if (writeLength > maxBufferLength)
        {
            writeLength = (int)maxBufferLength;
        }

        Buffer.BlockCopy(textBytes, 0, byteArray, (int)offset, writeLength);
    }

    internal static void WriteUnicodeString(byte[] byteArray, uint offset, string text, uint? maxBufferLength = null)
    {
        if (maxBufferLength != null)
        {
            Array.Clear(byteArray, (int)offset, (int)maxBufferLength);
        }

        var textBytes = Encoding.Unicode.GetBytes(text);
        var writeLength = textBytes.Length;
        if (writeLength > maxBufferLength)
        {
            writeLength = (int)maxBufferLength;
        }

        Buffer.BlockCopy(textBytes, 0, byteArray, (int)offset, writeLength);
    }

    internal static uint ReadUInt32(byte[] byteArray, uint offset)
    {
        return BitConverter.ToUInt32(byteArray, (int)offset);
    }

    internal static void WriteUInt32(byte[] byteArray, uint offset, uint value)
    {
        Buffer.BlockCopy(BitConverter.GetBytes(value), 0, byteArray, (int)offset, 4);
    }

    internal static int ReadInt32(byte[] byteArray, uint offset)
    {
        return BitConverter.ToInt32(byteArray, (int)offset);
    }

    internal static void WriteInt32(byte[] byteArray, uint offset, int value)
    {
        Buffer.BlockCopy(BitConverter.GetBytes(value), 0, byteArray, (int)offset, 4);
    }

    internal static ushort ReadUInt16(byte[] byteArray, uint offset)
    {
        return BitConverter.ToUInt16(byteArray, (int)offset);
    }

    internal static void WriteUInt16(byte[] byteArray, uint offset, ushort value)
    {
        Buffer.BlockCopy(BitConverter.GetBytes(value), 0, byteArray, (int)offset, 2);
    }

    internal static short ReadInt16(byte[] byteArray, uint offset)
    {
        return BitConverter.ToInt16(byteArray, (int)offset);
    }

    internal static void WriteInt16(byte[] byteArray, uint offset, short value)
    {
        Buffer.BlockCopy(BitConverter.GetBytes(value), 0, byteArray, (int)offset, 2);
    }

    internal static byte ReadUInt8(byte[] byteArray, uint offset)
    {
        return byteArray[offset];
    }

    internal static void WriteUInt8(byte[] byteArray, uint offset, byte value)
    {
        byteArray[offset] = value;
    }

    internal static uint ReadUInt24(byte[] byteArray, uint offset)
    {
        return (uint)(byteArray[offset] + (byteArray[offset + 1] << 8) + (byteArray[offset + 2] << 16));
    }

    internal static void WriteUInt24(byte[] byteArray, uint offset, uint value)
    {
        Buffer.BlockCopy(BitConverter.GetBytes(value), 0, byteArray, (int)offset, 3);
    }

    internal static ulong ReadUInt64(byte[] byteArray, uint offset)
    {
        return BitConverter.ToUInt64(byteArray, (int)offset);
    }

    internal static void WriteUInt64(byte[] byteArray, uint offset, ulong value)
    {
        Buffer.BlockCopy(BitConverter.GetBytes(value), 0, byteArray, (int)offset, 8);
    }

    internal static Guid ReadGuid(byte[] byteArray, uint offset)
    {
        var guidBuffer = new byte[0x10];
        Buffer.BlockCopy(byteArray, (int)offset, guidBuffer, 0, 0x10);
        return new(guidBuffer);
    }

    internal static void WriteGuid(byte[] byteArray, uint offset, Guid value)
    {
        Buffer.BlockCopy(value.ToByteArray(), 0, byteArray, (int)offset, 0x10);
    }

    internal static uint Align(uint @base, uint offset, uint alignment)
    {
        return (offset - @base) % alignment == 0 ? offset : ((((offset - @base) / alignment) + 1) * alignment) + @base;
    }

    internal static uint? FindAscii(byte[] sourceBuffer, string pattern)
    {
        return FindPattern(sourceBuffer, Encoding.ASCII.GetBytes(pattern), null, null);
    }

    internal static uint? FindUnicode(byte[] sourceBuffer, string pattern)
    {
        return FindPattern(sourceBuffer, Encoding.Unicode.GetBytes(pattern), null, null);
    }

    internal static uint? FindUint(byte[] sourceBuffer, uint pattern)
    {
        return FindPattern(sourceBuffer, BitConverter.GetBytes(pattern), null, null);
    }

    internal static uint? FindPattern(byte[] sourceBuffer, byte[] pattern, byte[]? mask, byte[]? outPattern)
    {
        return FindPattern(sourceBuffer, 0, null, pattern, mask, outPattern);
    }

    internal static bool Compare(byte[] array1, byte[] array2)
    {
        return StructuralComparisons.StructuralEqualityComparer.Equals(array1, array2);
    }

    internal static uint? FindPattern(byte[] sourceBuffer, uint sourceOffset, uint? sourceSize, byte[] pattern, byte[]? mask, byte[]? outPattern)
    {
        // The mask is optional.
        // In the mask 0x00 means the value must match, and 0xFF means that this position is a wildcard.

        uint? result = null;

        var searchPosition = sourceOffset;
        int i;

        while (searchPosition <= sourceBuffer.Length - pattern.Length && (sourceSize == null || searchPosition <= sourceOffset + sourceSize - pattern.Length))
        {
            var match = true;
            for (i = 0; i < pattern.Length; i++)
            {
                if (sourceBuffer[searchPosition + i] != pattern[i])
                {
                    if (mask == null || mask[i] == 0)
                    {
                        match = false;
                        break;
                    }
                }
            }

            if (match)
            {
                result = searchPosition;

                if (outPattern != null)
                {
                    Buffer.BlockCopy(sourceBuffer, (int)searchPosition, outPattern, 0, pattern.Length);
                }

                break;
            }

            searchPosition++;
        }

        return result;
    }

    internal static byte CalculateChecksum8(byte[] buffer, uint offset, uint size)
    {
        byte checksum = 0;

        for (var i = offset; i < offset + size; i++)
        {
            checksum += buffer[i];
        }

        return (byte)(0x100 - checksum);
    }

    internal static ushort CalculateChecksum16(byte[] buffer, uint offset, uint size)
    {
        ushort checksum = 0;

        for (var i = offset; i < offset + size - 1; i += 2)
        {
            checksum += BitConverter.ToUInt16(buffer, (int)i);
        }

        return (ushort)(0x10000 - checksum);
    }
}