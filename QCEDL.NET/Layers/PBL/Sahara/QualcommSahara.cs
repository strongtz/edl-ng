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

using QCEDL.NET.Logging;
using Qualcomm.EmergencyDownload.Layers.PBL.Sahara.Command;
using Qualcomm.EmergencyDownload.Transport;

namespace Qualcomm.EmergencyDownload.Layers.PBL.Sahara;

internal delegate void ReadyHandler();

public class QualcommSahara(IQualcommTransport transport)
{
    private const uint FirehoseProgrammerImageId = 13;
    public uint DetectedDeviceSaharaVersion { get; private set; } = 2;
    private Dictionary<uint, string> _imageMappings = [];

    #region Packet Building Logic

    public static byte[] BuildCommandPacket(QualcommSaharaCommand saharaCommand, byte[]? commandBuffer = null)
    {
        var commandId = (uint)saharaCommand;
        var commandBufferLength = (uint)(commandBuffer?.Length ?? 0);
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

    private static byte[] BuildHelloResponsePacket(QualcommSaharaMode saharaMode, uint protocolVersion = 2, uint supportedVersion = 1, uint maxPacketLength = 0)
    {
        var hello = new byte[0x28];

        // Hello packet:
        // xxxxxxxx = Protocol version
        // xxxxxxxx = Supported version
        // xxxxxxxx = Max packet length
        // xxxxxxxx = Expected mode
        // 6 dwords reserved space
        ByteOperations.WriteUInt32(hello, 0x00, protocolVersion);
        ByteOperations.WriteUInt32(hello, 0x04, supportedVersion);
        ByteOperations.WriteUInt32(hello, 0x08, maxPacketLength);
        ByteOperations.WriteUInt32(hello, 0x0C, (uint)saharaMode);
        return BuildCommandPacket(QualcommSaharaCommand.HelloResponse, hello);
    }

    private static bool IsFirehoseXml(ReadOnlySpan<byte> packet)
    {
        return packet.StartsWith("<?xml"u8);
    }

    private (byte[] Packet, bool HelloResponseSent) ReadInitialPacket(QualcommSaharaMode mode)
    {
        try
        {
            return (transport.GetResponse(null), false);
        }
        catch (TimeoutException) when (transport.Backend == TransportBackend.WindowsQud)
        {
            return RecoverDiscardedQudHello(mode);
        }
    }

    private (byte[] Packet, bool HelloResponseSent) RecoverDiscardedQudHello(QualcommSaharaMode mode)
    {
        LibraryLogger.Warning(
            $"Initial Sahara read timed out on Windows QUD; sending a speculative HELLO response for {mode} mode.");
        transport.SendData(BuildHelloResponsePacket(mode));
        return (transport.GetResponse(null), true);
    }

    #endregion

    #region Data Transfer Core

    private void SendData64Bit(FileStream fileStream, byte[] readDataRequest)
    {
        var offset = ByteOperations.ReadUInt64(readDataRequest, 0x10);
        var length = ByteOperations.ReadUInt64(readDataRequest, 0x18);
        var imageBuffer = new byte[length];

        if (fileStream.Position != (long)offset)
        {
            _ = fileStream.Seek((long)offset, SeekOrigin.Begin);
        }

        fileStream.ReadExactly(imageBuffer, 0, (int)length);
        transport.SendData(imageBuffer);
    }

    private void SendData(FileStream fileStream, byte[] readDataRequest)
    {
        var offset = ByteOperations.ReadUInt32(readDataRequest, 0x0C);
        var length = ByteOperations.ReadUInt32(readDataRequest, 0x10);
        var imageBuffer = new byte[length];

        if (fileStream.Position != offset)
        {
            _ = fileStream.Seek(offset, SeekOrigin.Begin);
        }

        fileStream.ReadExactly(imageBuffer, 0, (int)length);
        transport.SendData(imageBuffer);
    }

    #endregion

    #region Public Interface

    public QualcommSaharaHandshakeResult ProbeCommandMode(
        byte[]? preReadHelloPacket = null,
        bool initialReadAlreadyTimedOut = false)
    {
        try
        {
            byte[] packet;
            var helloResponseSent = false;
            if (preReadHelloPacket != null && initialReadAlreadyTimedOut)
            {
                throw new ArgumentException(
                    "A pre-read HELLO packet cannot be combined with an already timed-out initial read.",
                    nameof(initialReadAlreadyTimedOut));
            }

            if (preReadHelloPacket != null)
            {
                LibraryLogger.Debug("Using pre-read HELLO packet for handshake.");
                packet = preReadHelloPacket;
            }
            else if (initialReadAlreadyTimedOut)
            {
                if (transport.Backend != TransportBackend.WindowsQud)
                {
                    throw new InvalidOperationException(
                        "Discarded HELLO recovery is only available for Windows QUD transports.");
                }

                (packet, helloResponseSent) = RecoverDiscardedQudHello(QualcommSaharaMode.Command);
            }
            else
            {
                LibraryLogger.Debug("Reading HELLO packet from device for handshake.");
                (packet, helloResponseSent) = ReadInitialPacket(QualcommSaharaMode.Command);
            }

            if (IsFirehoseXml(packet))
            {
                LibraryLogger.Debug("Device returned Firehose XML while probing Sahara command mode.");
                return QualcommSaharaHandshakeResult.Firehose;
            }

            if (packet.Length < sizeof(uint))
            {
                throw new BadMessageException("Initial Sahara response is shorter than a command ID.");
            }

            var command = (QualcommSaharaCommand)ByteOperations.ReadUInt32(packet, 0);
            if (helloResponseSent && command == QualcommSaharaCommand.CommandReady)
            {
                LibraryLogger.Debug("Windows QUD accepted the speculative HELLO response.");
                return QualcommSaharaHandshakeResult.Sahara;
            }

            if (command != QualcommSaharaCommand.Hello)
            {
                throw new BadMessageException($"Expected Sahara HELLO, received {command}.");
            }

            if (packet.Length < 0x0C)
            {
                throw new BadMessageException("Sahara HELLO packet is too short.");
            }

            DetectedDeviceSaharaVersion = ByteOperations.ReadUInt32(packet, 0x08);
            var helloResponse = BuildHelloResponsePacket(QualcommSaharaMode.Command);
            var ready = transport.SendCommand(helloResponse, null);
            if (IsFirehoseXml(ready))
            {
                LibraryLogger.Debug("Device returned Firehose XML after the Sahara HELLO response.");
                return QualcommSaharaHandshakeResult.Firehose;
            }

            if (ready.Length < sizeof(uint))
            {
                throw new BadMessageException("Sahara command-mode response is shorter than a command ID.");
            }

            var responseId = (QualcommSaharaCommand)ByteOperations.ReadUInt32(ready, 0);
            return responseId != QualcommSaharaCommand.CommandReady
                ? throw new BadMessageException($"Expected Sahara COMMAND_READY, received {responseId}.")
                : QualcommSaharaHandshakeResult.Sahara;
        }
        catch (Exception ex)
        {
            LibraryLogger.Error($"Handshake failed: {ex.Message}");
            return QualcommSaharaHandshakeResult.Failed;
        }
    }

    public bool CommandHandshake(byte[]? preReadHelloPacket = null)
    {
        return ProbeCommandMode(preReadHelloPacket) == QualcommSaharaHandshakeResult.Sahara;
    }

    public void ResetSahara()
    {
        _ = transport.SendCommand(BuildCommandPacket(QualcommSaharaCommand.Reset), [0x08, 0x00, 0x00, 0x00]);
    }

    public void SwitchMode(QualcommSaharaMode mode)
    {
        var switchMode = new byte[0x04];
        ByteOperations.WriteUInt32(switchMode, 0x00, (uint)mode);
        transport.SendData(BuildCommandPacket(QualcommSaharaCommand.SwitchMode, switchMode));
    }

    #endregion

    public bool SendImage(string path)
    {
        try
        {
            if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                _imageMappings = SaharaConfigParser.ParseAndValidateConfig(path);
                LibraryLogger.Debug("Multi-image mode validated.");
                LibraryLogger.Debug($"Loaded configuration, total {_imageMappings.Count} files.");
            }
            else
            {
                _imageMappings.Clear();
                _imageMappings[FirehoseProgrammerImageId] = path;
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("File not found", path);
                }
            }
        }
        catch (Exception ex)
        {
            LibraryLogger.Error($"Pre-transfer validation failed: {ex.Message}");
            return false;
        }

        var imagesTransferredCount = 0;
        try
        {
            var (initialPacket, helloResponseSent) = ReadInitialPacket(QualcommSaharaMode.ImageTxPending);
            if (IsFirehoseXml(initialPacket))
            {
                LibraryLogger.Debug("Device is already in Firehose mode; skipping Sahara image transfer.");
                return true;
            }

            if (initialPacket.Length < sizeof(uint))
            {
                throw new BadMessageException("Initial Sahara transfer response is shorter than a command ID.");
            }

            var pendingRequest = initialPacket;
            var initialCommand = (QualcommSaharaCommand)ByteOperations.ReadUInt32(initialPacket, 0);
            if (initialCommand == QualcommSaharaCommand.Hello)
            {
                if (initialPacket.Length < 0x0C)
                {
                    throw new BadMessageException("Sahara HELLO packet is too short.");
                }

                DetectedDeviceSaharaVersion = ByteOperations.ReadUInt32(initialPacket, 0x08);
                transport.SendData(BuildHelloResponsePacket(QualcommSaharaMode.ImageTxPending));
                pendingRequest = null;
            }
            else if (!helloResponseSent)
            {
                LibraryLogger.Debug($"Sahara transfer started with {initialCommand} instead of HELLO.");
            }

            while (true)
            {
                byte[] request;
                try
                {
                    request = pendingRequest ?? transport.GetResponse(null);
                    pendingRequest = null;
                }
                catch (TimeoutException)
                {
                    if (imagesTransferredCount > 0)
                    {
                        LibraryLogger.Debug("Sahara timeout after successful transfers. Device likely switched to Firehose mode.");
                        return true;
                    }
                    throw;
                }

                var commandId = (QualcommSaharaCommand)ByteOperations.ReadUInt32(request, 0);

                if (commandId == QualcommSaharaCommand.Hello)
                {
                    LibraryLogger.Debug("Device requested re-handshake (Next stage).");
                    transport.SendData(BuildHelloResponsePacket(QualcommSaharaMode.ImageTxPending));
                    continue;
                }

                if (commandId is QualcommSaharaCommand.ReadData or QualcommSaharaCommand.ReadData64Bit)
                {
                    var lastRequestedId = ByteOperations.ReadUInt32(request, 0x08);
                    if (_imageMappings.TryGetValue(lastRequestedId, out var filePath) && File.Exists(filePath))
                    {
                        LibraryLogger.Debug($"Requested ID: {lastRequestedId}, Path: {filePath}");
                        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        if (commandId == QualcommSaharaCommand.ReadData)
                        {
                            SendData(fs, request);
                        }
                        else
                        {
                            SendData64Bit(fs, request);
                        }
                    }
                    else
                    {
                        LibraryLogger.Error($"Could not find Image ID: {lastRequestedId}");
                        return false;
                    }
                }
                else if (commandId == QualcommSaharaCommand.EndImageTx)
                {
                    LibraryLogger.Debug("Image chunk transfer finished, sending DONE.");
                    transport.SendData(BuildCommandPacket(QualcommSaharaCommand.Done));
                    imagesTransferredCount++;
                }
                else if (commandId is QualcommSaharaCommand.Done or QualcommSaharaCommand.DoneResponse)
                {
                    if (imagesTransferredCount >= _imageMappings.Count)
                    {
                        LibraryLogger.Debug($"Received {commandId} from device. All {imagesTransferredCount} image(s) transferred, Sahara transfer complete.");
                        return true;
                    }

                    LibraryLogger.Debug($"Received {commandId} from device. Waiting for potential next stage...");
                    continue;
                }
                else
                {
                    LibraryLogger.Error($"Unexpected Sahara command received: 0x{(uint)commandId:X8}");
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            LibraryLogger.Error($"Transfer crash: {ex.Message}");
            return false;
        }
    }

    public Task<bool> LoadProgrammer(string programmerPath)
    {
        return Task.Run(() => SendImage(programmerPath));
    }

    #region Helper Methods for Device Info

    public byte[][] GetRkHs()
    {
        return Execute.GetRkHs(transport);
    }

    public byte[] GetRkh()
    {
        return Execute.GetRkh(transport);
    }

    public byte[] GetHwid()
    {
        return Execute.GetHwid(transport);
    }

    public byte[] GetSerialNumber()
    {
        return Execute.GetSerialNumber(transport);
    }

    #endregion
}