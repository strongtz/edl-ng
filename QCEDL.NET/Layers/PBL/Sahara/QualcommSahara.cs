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

using QCEDL.NET.Extensions;
using QCEDL.NET.Logging;
using Qualcomm.EmergencyDownload.Layers.PBL.Sahara.Command;
using Qualcomm.EmergencyDownload.Transport;

namespace Qualcomm.EmergencyDownload.Layers.PBL.Sahara;

internal delegate void ReadyHandler();

public class QualcommSahara
{
    private readonly QualcommSerial Serial;
    public uint DetectedDeviceSaharaVersion { get; private set; } = 2;

    public QualcommSahara(QualcommSerial Serial)
    {
        this.Serial = Serial;
    }

    public static byte[] BuildCommandPacket(QualcommSaharaCommand SaharaCommand, byte[]? CommandBuffer = null)
    {
        var CommandID = (uint)SaharaCommand;
        uint CommandBufferLength = 0;
        if (CommandBuffer != null)
        {
            CommandBufferLength = (uint)CommandBuffer.Length;
        }
        var Length = 0x8u + CommandBufferLength;

        var Packet = new byte[Length];
        ByteOperations.WriteUInt32(Packet, 0x00, CommandID);
        ByteOperations.WriteUInt32(Packet, 0x04, Length);

        if (CommandBuffer != null && CommandBufferLength != 0)
        {
            Buffer.BlockCopy(CommandBuffer, 0, Packet, 0x08, CommandBuffer.Length);
        }

        return Packet;
    }

    private static byte[] BuildHelloResponsePacket(QualcommSaharaMode SaharaMode, uint ProtocolVersion = 2, uint SupportedVersion = 1, uint MaxPacketLength = 0 /* 0: Status OK */)
    {
        var Mode = (uint)SaharaMode;

        // Hello packet:
        // xxxxxxxx = Protocol version
        // xxxxxxxx = Supported version
        // xxxxxxxx = Max packet length
        // xxxxxxxx = Expected mode
        // 6 dwords reserved space
        var Hello = new byte[0x28];
        ByteOperations.WriteUInt32(Hello, 0x00, ProtocolVersion);
        ByteOperations.WriteUInt32(Hello, 0x04, SupportedVersion);
        ByteOperations.WriteUInt32(Hello, 0x08, MaxPacketLength);
        ByteOperations.WriteUInt32(Hello, 0x0C, Mode);
        ByteOperations.WriteUInt32(Hello, 0x10, 0);
        ByteOperations.WriteUInt32(Hello, 0x14, 0);
        ByteOperations.WriteUInt32(Hello, 0x18, 0);
        ByteOperations.WriteUInt32(Hello, 0x1C, 0);
        ByteOperations.WriteUInt32(Hello, 0x20, 0);
        ByteOperations.WriteUInt32(Hello, 0x24, 0);

        return BuildCommandPacket(QualcommSaharaCommand.HelloResponse, Hello);
    }

    private void SendData64Bit(FileStream FileStream, byte[] ReadDataRequest)
    {
        var ImageID = ByteOperations.ReadUInt64(ReadDataRequest, 0x08);
        var Offset = ByteOperations.ReadUInt64(ReadDataRequest, 0x10);
        var Length = ByteOperations.ReadUInt64(ReadDataRequest, 0x18);

        var ImageBuffer = new byte[Length];

        if (FileStream.Position != (uint)Offset)
        {
            FileStream.Seek((uint)Offset, SeekOrigin.Begin);
        }

        FileStream.ReadExactly(ImageBuffer, 0, (int)Length);

        Serial.SendData(ImageBuffer);
    }

    private void SendData(FileStream FileStream, byte[] ReadDataRequest)
    {
        var ImageID = ByteOperations.ReadUInt32(ReadDataRequest, 0x08);
        var Offset = ByteOperations.ReadUInt32(ReadDataRequest, 0x0C);
        var Length = ByteOperations.ReadUInt32(ReadDataRequest, 0x10);

        var ImageBuffer = new byte[Length];

        if (FileStream.Position != Offset)
        {
            FileStream.Seek(Offset, SeekOrigin.Begin);
        }

        FileStream.ReadExactly(ImageBuffer, 0, (int)Length);

        Serial.SendData(ImageBuffer);
    }

    public bool SendImage(string Path)
    {
        var Result = true;

        LibraryLogger.Debug("Sending programmer: " + Path);

        //byte[]? ImageBuffer = null; //unused
        try
        {
            var Hello = Serial.GetResponse([0x01, 0x00, 0x00, 0x00]);

            // Incoming Hello packet:
            // 00000001 = Hello command id
            // xxxxxxxx = Length
            // xxxxxxxx = Protocol version
            // xxxxxxxx = Supported version
            // xxxxxxxx = Max packet length
            // xxxxxxxx = Expected mode
            // 6 dwords reserved space
            LibraryLogger.Debug("Protocol: 0x" + ByteOperations.ReadUInt32(Hello, 0x08).ToStringInvariantCulture("X8"));
            LibraryLogger.Debug("Supported: 0x" + ByteOperations.ReadUInt32(Hello, 0x0C).ToStringInvariantCulture("X8"));
            LibraryLogger.Debug("MaxLength: 0x" + ByteOperations.ReadUInt32(Hello, 0x10).ToStringInvariantCulture("X8"));
            LibraryLogger.Debug("Mode: 0x" + ByteOperations.ReadUInt32(Hello, 0x14).ToStringInvariantCulture("X8"));

            var HelloResponse = BuildHelloResponsePacket(QualcommSaharaMode.ImageTXPending);
            Serial.SendData(HelloResponse);

            using FileStream FileStream = new(Path, FileMode.Open, FileAccess.Read);

            var CommandID = QualcommSaharaCommand.NoCommand;

            while (CommandID != QualcommSaharaCommand.EndImageTX)
            {
                var ReadDataRequest = Serial.GetResponse(null);

                CommandID = (QualcommSaharaCommand)ByteOperations.ReadUInt32(ReadDataRequest, 0);

                switch (CommandID)
                {
                    // 32-Bit data request
                    case QualcommSaharaCommand.ReadData:
                        {
                            SendData(FileStream, ReadDataRequest);
                            break;
                        }
                    // 64-Bit data request
                    case QualcommSaharaCommand.ReadData64Bit:
                        {
                            SendData64Bit(FileStream, ReadDataRequest);
                            break;
                        }
                    // End Transfer
                    case QualcommSaharaCommand.EndImageTX:
                        {
                            break;
                        }
                    default:
                        {
                            LibraryLogger.Error($"Unknown command: {CommandID.ToString("X8")}");
                            throw new BadConnectionException();
                        }
                }
            }
        }
        catch (Exception Ex)
        {
            LibraryLogger.Error("An unexpected error happened");
            LibraryLogger.Error(Ex.GetType().ToString());
            LibraryLogger.Error(Ex.Message);
            LibraryLogger.Error(Ex.StackTrace);
            Result = false;
        }

        if (Result)
        {
            LibraryLogger.Debug("Programmer loaded into phone memory");
        }

        return Result;
    }

    public bool Handshake()
    {
        var Result = true;

        try
        {
            var Hello = Serial.GetResponse([0x01, 0x00, 0x00, 0x00]);

            // Incoming Hello packet:
            // 00000001 = Hello command id
            // xxxxxxxx = Length
            // xxxxxxxx = Protocol version
            // xxxxxxxx = Supported version
            // xxxxxxxx = Max packet length
            // xxxxxxxx = Expected mode
            // 6 dwords reserved space
            LibraryLogger.Debug("Protocol: 0x" + ByteOperations.ReadUInt32(Hello, 0x08).ToStringInvariantCulture("X8"));
            LibraryLogger.Debug("Supported: 0x" + ByteOperations.ReadUInt32(Hello, 0x0C).ToStringInvariantCulture("X8"));
            LibraryLogger.Debug("MaxLength: 0x" + ByteOperations.ReadUInt32(Hello, 0x10).ToStringInvariantCulture("X8"));
            LibraryLogger.Debug("Mode: 0x" + ByteOperations.ReadUInt32(Hello, 0x14).ToStringInvariantCulture("X8"));

            var HelloResponse = BuildHelloResponsePacket(QualcommSaharaMode.ImageTXPending);

            var Ready = Serial.SendCommand(HelloResponse, [0x03, 0x00, 0x00, 0x00]);
        }
        catch (Exception ex)
        {
            LibraryLogger.Error("An unexpected error happened");
            LibraryLogger.Error(ex.GetType().ToString());
            LibraryLogger.Error(ex.Message);
            LibraryLogger.Error(ex.StackTrace);

            Result = false;
        }

        return Result;
    }

    public bool CommandHandshake(byte[]? preReadHelloPacket = null)
    {
        var Result = true;
        byte[] Hello;

        try
        {
            if (preReadHelloPacket != null)
            {
                LibraryLogger.Debug("Using pre-read HELLO packet for handshake.");
                Hello = preReadHelloPacket;
                // Basic validation: check command ID
                if (Hello.Length < 4 || ByteOperations.ReadUInt32(Hello, 0) != (uint)QualcommSaharaCommand.Hello)
                {
                    LibraryLogger.Error("Pre-read packet is not a valid Sahara HELLO packet.");
                    throw new BadMessageException("Invalid pre-read HELLO packet.");
                }
            }
            else
            {
                LibraryLogger.Debug("Reading HELLO packet from device for handshake.");
                Hello = Serial.GetResponse([0x01, 0x00, 0x00, 0x00]);
            }

            // Incoming Hello packet:
            // 00000001 = Hello command id
            // xxxxxxxx = Length
            // xxxxxxxx = Protocol version
            // xxxxxxxx = Supported version
            // xxxxxxxx = Max packet length
            // xxxxxxxx = Expected mode
            // 6 dwords reserved space
            LibraryLogger.Debug("Protocol: 0x" + ByteOperations.ReadUInt32(Hello, 0x08).ToStringInvariantCulture("X8"));
            LibraryLogger.Debug("Supported: 0x" + ByteOperations.ReadUInt32(Hello, 0x0C).ToStringInvariantCulture("X8"));
            LibraryLogger.Debug("MaxLength: 0x" + ByteOperations.ReadUInt32(Hello, 0x10).ToStringInvariantCulture("X8"));
            LibraryLogger.Debug("Mode: 0x" + ByteOperations.ReadUInt32(Hello, 0x14).ToStringInvariantCulture("X8"));

            DetectedDeviceSaharaVersion = ByteOperations.ReadUInt32(Hello, 0x08);

            var HelloResponse = BuildHelloResponsePacket(QualcommSaharaMode.Command);

            var Ready = Serial.SendCommand(HelloResponse, null);

            var ResponseID = ByteOperations.ReadUInt32(Ready, 0);

            if (ResponseID != (uint)QualcommSaharaCommand.CommandReady)
            {
                LibraryLogger.Error($"Expected CommandReady (0x0B) but received {ResponseID:X2}.");
                throw new BadConnectionException($"Unexpected response after HelloResponse: {ResponseID:X2}");
            }
        }
        catch (BadMessageException bmEx)
        {
            LibraryLogger.Error($"Handshake failed due to bad message: {bmEx.Message}");
            Result = false;
        }
        catch (Exception ex)
        {
            LibraryLogger.Error("An unexpected error happened");
            LibraryLogger.Error(ex.GetType().ToString());
            LibraryLogger.Error(ex.Message);
            LibraryLogger.Error(ex.StackTrace);

            Result = false;
        }

        return Result;
    }

    public void ResetSahara()
    {
        Serial.SendCommand(BuildCommandPacket(QualcommSaharaCommand.Reset), [0x08, 0x00, 0x00, 0x00]);
    }

    public void SwitchMode(QualcommSaharaMode Mode)
    {
        var SwitchMode = new byte[0x04];
        ByteOperations.WriteUInt32(SwitchMode, 0x00, (uint)Mode);

        var SwitchModeCommand = BuildCommandPacket(QualcommSaharaCommand.SwitchMode, SwitchMode);

        byte[]? ResponsePattern = null;
        switch (Mode)
        {
            case QualcommSaharaMode.ImageTXPending:
                ResponsePattern = [0x04, 0x00, 0x00, 0x00];
                break;
            case QualcommSaharaMode.MemoryDebug:
                ResponsePattern = [0x09, 0x00, 0x00, 0x00];
                break;
            case QualcommSaharaMode.Command:
                ResponsePattern = [0x0B, 0x00, 0x00, 0x00];
                break;
        }

        Serial.SendData(SwitchModeCommand);
    }

    public void StartProgrammer()
    {
        LibraryLogger.Debug("Starting programmer");
        var DoneCommand = BuildCommandPacket(QualcommSaharaCommand.Done);

        var Started = false;
        var count = 0;

        do
        {
            count++;
            try
            {
                var DoneResponse = Serial.SendCommand(DoneCommand, [0x06, 0x00, 0x00, 0x00]);
                Started = true;
            }
            catch (BadConnectionException)
            {
                LibraryLogger.Error("Problem while starting programmer. Attempting again.");
            }
        } while (!Started && count < 3);

        if (count >= 3 && !Started)
        {
            LibraryLogger.Error("Maximum number of attempts to start the programmer exceeded.");
            throw new BadConnectionException();
        }

        LibraryLogger.Debug("Programmer being launched on phone");
    }

    public async Task<bool> LoadProgrammer(string ProgrammerPath)
    {
        var SendImageResult = await Task.Run(() => SendImage(ProgrammerPath));

        if (!SendImageResult)
        {
            return false;
        }

        await Task.Run(StartProgrammer);

        return true;
    }


    public byte[][] GetRKHs()
    {
        return Execute.GetRKHs(Serial);
    }

    public byte[] GetRKH()
    {
        return Execute.GetRKH(Serial);
    }

    public byte[] GetHWID()
    {
        return Execute.GetHWID(Serial);
    }

    public byte[] GetSerialNumber()
    {
        return Execute.GetSerialNumber(Serial);
    }
}