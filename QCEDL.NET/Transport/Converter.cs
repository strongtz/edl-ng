﻿// Copyright (c) 2018, Rene Lergner - @Heathcliff74xda
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
//
// Some of the classes and functions in this file were found online.
// Where possible the original authors are referenced.

using System.Text;
using QCEDL.NET.Extensions;
using QCEDL.NET.Todo;

namespace Qualcomm.EmergencyDownload.Transport;

public static class Converter
{
    public static string ConvertHexToString(byte[] Bytes, string Separator)
    {
        StringBuilder s = new(1000);
        for (var i = Bytes.GetLowerBound(0); i <= Bytes.GetUpperBound(0); i++)
        {
            if (i != Bytes.GetLowerBound(0))
            {
                s.Append(Separator);
            }

            s.Append(Bytes[i].ToStringInvariantCulture("X2"));
        }
        return s.ToString();
    }

    public static byte[] ConvertStringToHex(string HexString)
    {
        if (HexString.Length % 2 == 1)
        {
            throw new TodoException("The binary key cannot have an odd number of digits");
        }

        var arr = new byte[HexString.Length >> 1];

        for (var i = 0; i < HexString.Length >> 1; ++i)
        {
            arr[i] = (byte)((GetHexVal(HexString[i << 1]) << 4) + GetHexVal(HexString[(i << 1) + 1]));
        }

        return arr;
    }

    public static int GetHexVal(char hex)
    {
        int val = hex;
        //For uppercase A-F letters:
        //return val - (val < 58 ? 48 : 55);
        //For lowercase a-f letters:
        //return val - (val < 58 ? 48 : 87);
        //Or the two combined, but a bit slower:
        return val - (val < 58 ? 48 : val < 97 ? 55 : 87);
    }
}