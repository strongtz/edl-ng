/*  WinUSBNet library
 *  (C) 2010 Thomas Bleeker (www.madwizard.org)
 *
 *  Licensed under the MIT license, see license.txt or:
 *  http://www.opensource.org/licenses/mit-license.php
 */

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

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using QCEDL.NET.Todo;

namespace QCEDL.NET.USB;

public class UsbExtensions
{
    public static (string? PathName, string BusName, int DevInst)[] GetDeviceInfos(Guid id)
    {
        List<(string? PathName, string BusName, int DevInst)> deviceInfos = [];
        var deviceInfoSet = nint.Zero;
        try
        {
            deviceInfoSet = SetupDiGetClassDevs(
                ref id,
                nint.Zero,
                nint.Zero,
                DigcfPresent | DigcfDeviceinterface
            );
            if (deviceInfoSet == InvalidHandleValue)
            {
                var error = Marshal.GetLastWin32Error();
                return error != 0
                    ? throw new Win32Exception(error, "Failed to get device class info set.")
                    : [];
            }
            var memberIndex = 0;
            while (true)
            {
                SpDeviceInterfaceData deviceInterfaceData = new();
                deviceInterfaceData.CbSize = Marshal.SizeOf(deviceInterfaceData);
                var success = SetupDiEnumDeviceInterfaces(
                    deviceInfoSet,
                    nint.Zero,
                    ref id,
                    memberIndex,
                    ref deviceInterfaceData
                );
                if (!success)
                {
                    var lastError = Marshal.GetLastWin32Error();
                    if (lastError == ErrorNoMoreItems)
                    {
                        break;
                    }
                    Debug.WriteLine(
                        $"SetupDiEnumDeviceInterfaces failed with error: {lastError} for memberIndex: {memberIndex}"
                    );
                    memberIndex++;
                    continue;
                }
                var bufferSize = 0;

                SpDevinfoData da = new();
                da.CbSize = Marshal.SizeOf(da);

                success = SetupDiGetDeviceInterfaceDetail(
                    deviceInfoSet,
                    out deviceInterfaceData,
                    nint.Zero,
                    0,
                    ref bufferSize,
                    ref da
                );

                if (!success && Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
                {
                    Debug.WriteLine(
                        $"Failed to get interface details buffer size. Error: {Marshal.GetLastWin32Error()}"
                    );
                    memberIndex++;
                    continue;
                }

                if (bufferSize == 0)
                {
                    Debug.WriteLine("Buffer size for device interface detail is 0. Skipping.");
                    memberIndex++;
                    continue;
                }
                var detailDataBuffer = nint.Zero;
                try
                {
                    detailDataBuffer = Marshal.AllocHGlobal(bufferSize);
                    Marshal.WriteInt32(
                        detailDataBuffer,
                        nint.Size == 4 ? 4 + Marshal.SystemDefaultCharSize : 8
                    );
                    da.CbSize = Marshal.SizeOf(da);
                    success = SetupDiGetDeviceInterfaceDetail(
                        deviceInfoSet,
                        out deviceInterfaceData,
                        detailDataBuffer,
                        bufferSize,
                        ref bufferSize,
                        ref da
                    );
                    if (!success)
                    {
                        Debug.WriteLine(
                            $"Failed to get device interface details. Error: {Marshal.GetLastWin32Error()}"
                        );
                        memberIndex++;
                        continue;
                    }
                    nint pDevicePathName = new(detailDataBuffer.ToInt64() + 4);
                    var pathName = Marshal.PtrToStringUni(pDevicePathName);
                    var busName = GetBusName(pathName, deviceInfoSet, da);
                    deviceInfos.Add((pathName, busName, da.DevInst));
                }
                finally
                {
                    if (detailDataBuffer != nint.Zero)
                    {
                        Marshal.FreeHGlobal(detailDataBuffer);
                    }
                }
                memberIndex++;
            }
        }
        finally
        {
            if (deviceInfoSet != nint.Zero && deviceInfoSet != InvalidHandleValue)
            {
                // TODO: Check return value
                _ = SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }
        }
        return [.. deviceInfos];
    }

    private static string GetBusName(string? _, nint deviceInfoSet, SpDevinfoData deviceInfoData)
    {
        var busName = "";

        try
        {
            busName = GetStringProperty(
                deviceInfoSet,
                deviceInfoData,
                new(
                    new(0x540b947e, 0x8b40, 0x45bc, 0xa8, 0xa2, 0x6a, 0x0b, 0x89, 0x4c, 0xbd, 0xa2),
                    4
                )
            );
        }
        catch (Exception)
        {
            // ignored
        }

        return busName;
    }

    // Heathcliff74
    // todo: is the queried data always available, or should we check ERROR_INVALID_DATA?
    private static string GetStringProperty(
        nint deviceInfoSet,
        SpDevinfoData deviceInfoData,
        Devpropkey property
    )
    {
        var buffer = GetProperty(deviceInfoSet, deviceInfoData, property, out var propertyType);
        if (propertyType != 0x00000012) // DEVPROP_TYPE_STRING
        {
            throw new TodoException("Invalid registry type returned for device property.");
        }

        // sizeof(char), 2 bytes, are removed to leave out the string terminator
        return Encoding.Unicode.GetString(buffer, 0, buffer.Length - sizeof(char));
    }

    // Heathcliff74
    private static byte[] GetProperty(
        nint deviceInfoSet,
        SpDevinfoData deviceInfoData,
        Devpropkey property,
        out uint propertyType
    )
    {
        var requiredSize = 0;

        if (
            !SetupDiGetDeviceProperty(
                deviceInfoSet,
                ref deviceInfoData,
                ref property,
                out propertyType,
                null,
                0,
                ref requiredSize,
                0
            )
            && Marshal.GetLastWin32Error() != ErrorInsufficientBuffer
        )
        {
            throw new Win32Exception("Failed to get buffer size for device registry property.");
        }

        var buffer = new byte[requiredSize];

        return !SetupDiGetDeviceProperty(
            deviceInfoSet,
            ref deviceInfoData,
            ref property,
            out propertyType,
            buffer,
            buffer.Length,
            ref requiredSize,
            0
        )
            ? throw new Win32Exception("Failed to get device registry property.")
            : buffer;
    }

    private const int DigcfPresent = 2;
    private const int DigcfDeviceinterface = 0X10;
    private const nint InvalidHandleValue = -1;
    private const int ErrorNoMoreItems = 259;
    private const int ErrorInsufficientBuffer = 122;

#pragma warning disable CS0649 // Field 'field' is never assigned to, and will always have its default value 'value'
    private struct SpDeviceInterfaceData
    {
        internal int CbSize;
        internal Guid InterfaceClassGuid;
        internal int Flags;
        internal nint Reserved;
    }

    private struct SpDevinfoData
    {
        internal int CbSize;
        internal Guid ClassGuid;
        internal int DevInst;
        internal nint Reserved;
    }
#pragma warning restore CS0649 // Field 'field' is never assigned to, and will always have its default value 'value'

    // Device Property
    [StructLayout(LayoutKind.Sequential)]
    private struct Devpropkey(Guid ifmtid, uint ipid)
    {
        public Guid FmtId = ifmtid;
        public uint Pid = ipid;
    }

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        nint deviceInfoSet,
        out SpDeviceInterfaceData deviceInterfaceData,
        nint deviceInterfaceDetailData,
        int deviceInterfaceDetailDataSize,
        ref int requiredSize,
        nint deviceInfoData
    );

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        nint deviceInfoSet,
        out SpDeviceInterfaceData deviceInterfaceData,
        nint deviceInterfaceDetailData,
        int deviceInterfaceDetailDataSize,
        ref int requiredSize,
        ref SpDevinfoData deviceInfoData
    );

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        nint deviceInfoSet,
        nint deviceInfoData,
        ref Guid interfaceClassGuid,
        int memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData
    );

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern int SetupDiDestroyDeviceInfoList(nint deviceInfoSet);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint SetupDiGetClassDevs(
        ref Guid classGuid,
        nint enumerator,
        nint hwndParent,
        int flags
    );

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceProperty(
        nint deviceInfoSet,
        ref SpDevinfoData deviceInfoData,
        ref Devpropkey propertyKey,
        out uint propertyType,
        byte[]? propertyBuffer,
        int propertyBufferSize,
        ref int requiredSize,
        uint flags
    );
}