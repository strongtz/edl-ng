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

using QCEDL.NET.Extensions;
using QCEDL.NET.Logging;
using Qualcomm.EmergencyDownload.Layers.PBL.Sahara.Command;
using Qualcomm.EmergencyDownload.Transport;

namespace Qualcomm.EmergencyDownload.Layers.PBL.Sahara;

internal delegate void ReadyHandler();

public class QualcommSahara(QualcommSerial serial)
{
    public uint DetectedDeviceSaharaVersion { get; private set; } = 2;

    public static byte[] BuildCommandPacket(QualcommSaharaCommand saharaCommand, byte[]? commandBuffer = null)
    {
        var commandId = (uint)saharaCommand;
        uint commandBufferLength = 0;
        if (commandBuffer != null)
        {
            commandBufferLength = (uint)commandBuffer.Length;
        }
        var length = 0x8u + commandBufferLength;

        var packet = new byte[length];
        ByteOperations.WriteUInt32(packet, 0x00, commandId);
        ByteOperations.WriteUInt32(packet, 0x04, length);

        if (commandBuffer != null && commandBufferLength != 0)
        {
            Buffer.BlockCopy(commandBuffer, 0, packet, 0x08, commandBuffer.Length);
        }

        return packet;
    }

    private static byte[] BuildHelloResponsePacket(QualcommSaharaMode saharaMode, uint protocolVersion = 2, uint supportedVersion = 1, uint maxPacketLength = 0 /* 0: Status OK */)
    {
        var mode = (uint)saharaMode;

        // Hello packet:
        // xxxxxxxx = Protocol version
        // xxxxxxxx = Supported version
        // xxxxxxxx = Max packet length
        // xxxxxxxx = Expected mode
        // 6 dwords reserved space
        var hello = new byte[0x28];
        ByteOperations.WriteUInt32(hello, 0x00, protocolVersion);
        ByteOperations.WriteUInt32(hello, 0x04, supportedVersion);
        ByteOperations.WriteUInt32(hello, 0x08, maxPacketLength);
        ByteOperations.WriteUInt32(hello, 0x0C, mode);
        ByteOperations.WriteUInt32(hello, 0x10, 0);
        ByteOperations.WriteUInt32(hello, 0x14, 0);
        ByteOperations.WriteUInt32(hello, 0x18, 0);
        ByteOperations.WriteUInt32(hello, 0x1C, 0);
        ByteOperations.WriteUInt32(hello, 0x20, 0);
        ByteOperations.WriteUInt32(hello, 0x24, 0);

        return BuildCommandPacket(QualcommSaharaCommand.HelloResponse, hello);
    }

    private void SendData64Bit(FileStream fileStream, byte[] readDataRequest)
    {
        _ = ByteOperations.ReadUInt64(readDataRequest, 0x08);
        var offset = ByteOperations.ReadUInt64(readDataRequest, 0x10);
        var length = ByteOperations.ReadUInt64(readDataRequest, 0x18);

        var imageBuffer = new byte[length];

        if (fileStream.Position != (uint)offset)
        {
            _ = fileStream.Seek((uint)offset, SeekOrigin.Begin);
        }

        fileStream.ReadExactly(imageBuffer, 0, (int)length);

        serial.SendData(imageBuffer);
    }

    private void SendData(FileStream fileStream, byte[] readDataRequest)
    {
        _ = ByteOperations.ReadUInt32(readDataRequest, 0x08);
        var offset = ByteOperations.ReadUInt32(readDataRequest, 0x0C);
        var length = ByteOperations.ReadUInt32(readDataRequest, 0x10);

        var imageBuffer = new byte[length];

        if (fileStream.Position != offset)
        {
            _ = fileStream.Seek(offset, SeekOrigin.Begin);
        }

        fileStream.ReadExactly(imageBuffer, 0, (int)length);

        serial.SendData(imageBuffer);
    }

    public bool SendImage(string path)
    {
        var result = true;

        LibraryLogger.Debug("Sending programmer: " + path);

        //byte[]? ImageBuffer = null; //unused
        try
        {
            var hello = serial.GetResponse([0x01, 0x00, 0x00, 0x00]);

            // Incoming Hello packet:
            // 00000001 = Hello command id
            // xxxxxxxx = Length
            // xxxxxxxx = Protocol version
            // xxxxxxxx = Supported version
            // xxxxxxxx = Max packet length
            // xxxxxxxx = Expected mode
            // 6 dwords reserved space
            LibraryLogger.Debug("Protocol: 0x" + ByteOperations.ReadUInt32(hello, 0x08).ToStringInvariantCulture("X8"));
            LibraryLogger.Debug("Supported: 0x" + ByteOperations.ReadUInt32(hello, 0x0C).ToStringInvariantCulture("X8"));
            LibraryLogger.Debug("MaxLength: 0x" + ByteOperations.ReadUInt32(hello, 0x10).ToStringInvariantCulture("X8"));
            LibraryLogger.Debug("Mode: 0x" + ByteOperations.ReadUInt32(hello, 0x14).ToStringInvariantCulture("X8"));

            var helloResponse = BuildHelloResponsePacket(QualcommSaharaMode.ImageTxPending);
            serial.SendData(helloResponse);

            using FileStream fileStream = new(path, FileMode.Open, FileAccess.Read);

            var commandId = QualcommSaharaCommand.NoCommand;

            while (commandId != QualcommSaharaCommand.EndImageTx)
            {
                var readDataRequest = serial.GetResponse(null);

                commandId = (QualcommSaharaCommand)ByteOperations.ReadUInt32(readDataRequest, 0);

                switch (commandId)
                {
                    // 32-Bit data request
                    case QualcommSaharaCommand.ReadData:
                        {
                            SendData(fileStream, readDataRequest);
                            break;
                        }
                    // 64-Bit data request
                    case QualcommSaharaCommand.ReadData64Bit:
                        {
                            SendData64Bit(fileStream, readDataRequest);
                            break;
                        }
                    // End Transfer
                    case QualcommSaharaCommand.EndImageTx:
                        {
                            break;
                        }
                    case QualcommSaharaCommand.NoCommand:
                    case QualcommSaharaCommand.Hello:
                    case QualcommSaharaCommand.HelloResponse:
                    case QualcommSaharaCommand.Done:
                    case QualcommSaharaCommand.DoneResponse:
                    case QualcommSaharaCommand.Reset:
                    case QualcommSaharaCommand.ResetResponse:
                    case QualcommSaharaCommand.MemoryDebug:
                    case QualcommSaharaCommand.MemoryRead:
                    case QualcommSaharaCommand.CommandReady:
                    case QualcommSaharaCommand.SwitchMode:
                    case QualcommSaharaCommand.Execute:
                    case QualcommSaharaCommand.ExecuteResponse:
                    case QualcommSaharaCommand.ExecuteData:
                    case QualcommSaharaCommand.MemoryDebug64Bit:
                    case QualcommSaharaCommand.MemoryRead64Bit:
                    case QualcommSaharaCommand.ResetStateMachine:
                    default:
                        {
                            LibraryLogger.Error($"Unknown command: {commandId:X8}");
                            throw new BadConnectionException();
                        }
                }
            }
        }
        catch (Exception ex)
        {
            LibraryLogger.Error("An unexpected error happened");
            LibraryLogger.Error(ex.GetType().ToString());
            LibraryLogger.Error(ex.Message);
            LibraryLogger.Error(ex.StackTrace);
            result = false;
        }

        if (result)
        {
            LibraryLogger.Debug("Programmer loaded into phone memory");
        }

        return result;
    }

    public bool Handshake()
    {
        var result = true;

        try
        {
            var hello = serial.GetResponse([0x01, 0x00, 0x00, 0x00]);

            // Incoming Hello packet:
            // 00000001 = Hello command id
            // xxxxxxxx = Length
            // xxxxxxxx = Protocol version
            // xxxxxxxx = Supported version
            // xxxxxxxx = Max packet length
            // xxxxxxxx = Expected mode
            // 6 dwords reserved space
            LibraryLogger.Debug("Protocol: 0x" + ByteOperations.ReadUInt32(hello, 0x08).ToStringInvariantCulture("X8"));
            LibraryLogger.Debug("Supported: 0x" + ByteOperations.ReadUInt32(hello, 0x0C).ToStringInvariantCulture("X8"));
            LibraryLogger.Debug("MaxLength: 0x" + ByteOperations.ReadUInt32(hello, 0x10).ToStringInvariantCulture("X8"));
            LibraryLogger.Debug("Mode: 0x" + ByteOperations.ReadUInt32(hello, 0x14).ToStringInvariantCulture("X8"));

            var helloResponse = BuildHelloResponsePacket(QualcommSaharaMode.ImageTxPending);

            var ready = serial.SendCommand(helloResponse, [0x03, 0x00, 0x00, 0x00]);
        }
        catch (Exception ex)
        {
            LibraryLogger.Error("An unexpected error happened");
            LibraryLogger.Error(ex.GetType().ToString());
            LibraryLogger.Error(ex.Message);
            LibraryLogger.Error(ex.StackTrace);

            result = false;
        }

        return result;
    }

    public bool CommandHandshake(byte[]? preReadHelloPacket = null)
    {
        var result = true;
        byte[] hello;

        try
        {
            if (preReadHelloPacket != null)
            {
                LibraryLogger.Debug("Using pre-read HELLO packet for handshake.");
                hello = preReadHelloPacket;
                // Basic validation: check command ID
                if (hello.Length < 4 || ByteOperations.ReadUInt32(hello, 0) != (uint)QualcommSaharaCommand.Hello)
                {
                    LibraryLogger.Error("Pre-read packet is not a valid Sahara HELLO packet.");
                    throw new BadMessageException("Invalid pre-read HELLO packet.");
                }
            }
            else
            {
                LibraryLogger.Debug("Reading HELLO packet from device for handshake.");
                hello = serial.GetResponse([0x01, 0x00, 0x00, 0x00]);
            }

            // Incoming Hello packet:
            // 00000001 = Hello command id
            // xxxxxxxx = Length
            // xxxxxxxx = Protocol version
            // xxxxxxxx = Supported version
            // xxxxxxxx = Max packet length
            // xxxxxxxx = Expected mode
            // 6 dwords reserved space
            LibraryLogger.Debug("Protocol: 0x" + ByteOperations.ReadUInt32(hello, 0x08).ToStringInvariantCulture("X8"));
            LibraryLogger.Debug("Supported: 0x" + ByteOperations.ReadUInt32(hello, 0x0C).ToStringInvariantCulture("X8"));
            LibraryLogger.Debug("MaxLength: 0x" + ByteOperations.ReadUInt32(hello, 0x10).ToStringInvariantCulture("X8"));
            LibraryLogger.Debug("Mode: 0x" + ByteOperations.ReadUInt32(hello, 0x14).ToStringInvariantCulture("X8"));

            DetectedDeviceSaharaVersion = ByteOperations.ReadUInt32(hello, 0x08);

            var helloResponse = BuildHelloResponsePacket(QualcommSaharaMode.Command);

            var ready = serial.SendCommand(helloResponse, null);

            var responseId = ByteOperations.ReadUInt32(ready, 0);

            if (responseId != (uint)QualcommSaharaCommand.CommandReady)
            {
                LibraryLogger.Error($"Expected CommandReady (0x0B) but received {responseId:X2}.");
                throw new BadConnectionException($"Unexpected response after HelloResponse: {responseId:X2}");
            }
        }
        catch (BadMessageException bmEx)
        {
            LibraryLogger.Error($"Handshake failed due to bad message: {bmEx.Message}");
            result = false;
        }
        catch (Exception ex)
        {
            LibraryLogger.Error("An unexpected error happened");
            LibraryLogger.Error(ex.GetType().ToString());
            LibraryLogger.Error(ex.Message);
            LibraryLogger.Error(ex.StackTrace);

            result = false;
        }

        return result;
    }

    public void ResetSahara()
    {
        _ = serial.SendCommand(BuildCommandPacket(QualcommSaharaCommand.Reset), [0x08, 0x00, 0x00, 0x00]);
    }

    public void SwitchMode(QualcommSaharaMode mode)
    {
        var switchMode = new byte[0x04];
        ByteOperations.WriteUInt32(switchMode, 0x00, (uint)mode);

        var switchModeCommand = BuildCommandPacket(QualcommSaharaCommand.SwitchMode, switchMode);

        // Seems unused
        // byte[]? responsePattern = null;
        // switch (mode)
        // {
        //     case QualcommSaharaMode.ImageTxPending:
        //         responsePattern = [0x04, 0x00, 0x00, 0x00];
        //         break;
        //     case QualcommSaharaMode.MemoryDebug:
        //         responsePattern = [0x09, 0x00, 0x00, 0x00];
        //         break;
        //     case QualcommSaharaMode.Command:
        //         responsePattern = [0x0B, 0x00, 0x00, 0x00];
        //         break;
        //     case QualcommSaharaMode.ImageTxComplete:
        //     default:
        //         break;
        // }

        serial.SendData(switchModeCommand);
    }

    public void StartProgrammer()
    {
        LibraryLogger.Debug("Starting programmer");
        var doneCommand = BuildCommandPacket(QualcommSaharaCommand.Done);

        var started = false;
        var count = 0;

        do
        {
            count++;
            try
            {
                var doneResponse = serial.SendCommand(doneCommand, [0x06, 0x00, 0x00, 0x00]);
                started = true;
            }
            catch (BadConnectionException)
            {
                LibraryLogger.Error("Problem while starting programmer. Attempting again.");
            }
        } while (!started && count < 3);

        if (count >= 3 && !started)
        {
            LibraryLogger.Error("Maximum number of attempts to start the programmer exceeded.");
            throw new BadConnectionException();
        }

        LibraryLogger.Debug("Programmer being launched on phone");
    }

    public async Task<bool> LoadProgrammer(string programmerPath)
    {
        var sendImageResult = await Task.Run(() => SendImage(programmerPath));

        if (!sendImageResult)
        {
            return false;
        }

        await Task.Run(StartProgrammer);

        return true;
    }


    public byte[][] GetRkHs()
    {
        return Execute.GetRkHs(serial);
    }

    public byte[] GetRkh()
    {
        return Execute.GetRkh(serial);
    }

    public byte[] GetHwid()
    {
        return Execute.GetHwid(serial);
    }

    public byte[] GetSerialNumber()
    {
        return Execute.GetSerialNumber(serial);
    }
}