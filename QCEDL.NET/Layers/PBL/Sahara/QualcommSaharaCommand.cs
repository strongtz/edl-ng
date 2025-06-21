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

namespace Qualcomm.EmergencyDownload.Layers.PBL.Sahara;

public enum QualcommSaharaCommand : uint
{
    NoCommand = 0x00,
    Hello = 0x01,
    HelloResponse = 0x02,
    ReadData = 0x03,
    EndImageTx = 0x04,
    Done = 0x05,
    DoneResponse = 0x06,
    Reset = 0x07,
    ResetResponse = 0x08,
    MemoryDebug = 0x09,
    MemoryRead = 0x0A,
    CommandReady = 0x0B,
    SwitchMode = 0x0C,
    Execute = 0x0D,
    ExecuteResponse = 0x0E,
    ExecuteData = 0x0F,
    MemoryDebug64Bit = 0x10,
    MemoryRead64Bit = 0x11,
    ReadData64Bit = 0x12,
    ResetStateMachine = 0x13
}