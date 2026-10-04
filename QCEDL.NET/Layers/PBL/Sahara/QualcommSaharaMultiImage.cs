using System.Globalization;
using QCEDL.NET.Logging;
using Qualcomm.EmergencyDownload.Transport;

namespace Qualcomm.EmergencyDownload.Layers.PBL.Sahara;

internal sealed class QualcommSaharaMultiImage(
    IQualcommTransport transport,
    IReadOnlyDictionary<uint, string> imageMappings,
    Func<IQualcommTransport?>? transportReconnector)
{
    private const int StageTimeoutMilliseconds = 60000;
    private const int PostProgrammerTimeoutMilliseconds = 2000;
    private const int MaxReconnectAttempts = 3;
    private const int MaxUnexpectedPackets = 8;
    private const int ReconnectPollAttempts = 24;
    private const int ReconnectPollIntervalMilliseconds = 500;
    private const uint FallbackProtocolVersion = 2;
    private const uint FirehoseProgrammerImageId = 13;
    private const int HelloPacketLength = 0x30;
    private const int EndImageTxPacketLength = 0x10;
    private const int DoneResponsePacketLength = 0x0C;
    private const uint ImageTxPending = 0x00;
    private const uint ImageTxComplete = 0x01;

    private readonly List<uint> _servedImageIds = [];
    private readonly bool _expectsProgrammerImage = imageMappings.ContainsKey(FirehoseProgrammerImageId);
    private IQualcommTransport _transport = transport;
    private uint _negotiatedVersion = FallbackProtocolVersion;
    private int _reconnectCount;
    private int _unexpectedPacketCount;
    private bool _speculativeHelloSent;
    private bool _reconnectedAtLeastOnce;

    public bool Run()
    {
        var originalTimeout = _transport.TimeoutMilliseconds;
        LibraryLogger.Debug(
            $"Multi-image transfer, {imageMappings.Count} image id(s) mapped: [{FormatImageIds(imageMappings.Keys.OrderBy(id => id))}].");

        try
        {
            while (true)
            {
                byte[] packet;
                try
                {
                    _transport.TimeoutMilliseconds = IsChainFinished()
                        ? PostProgrammerTimeoutMilliseconds
                        : StageTimeoutMilliseconds;
                    packet = _transport.GetResponse(null);
                }
                catch (TimeoutException)
                {
                    if (TrySpeculativeHelloResponse())
                    {
                        continue;
                    }

                    if (TryFinishAfterProgrammer("stopped talking Sahara"))
                    {
                        return true;
                    }

                    if (TryReattachToTarget($"no Sahara packet for {StageTimeoutMilliseconds} ms"))
                    {
                        continue;
                    }

                    return ReportIncompleteTransfer();
                }
                catch (BadMessageException ex)
                {
                    LibraryLogger.Warning($"Empty or unusable Sahara read: {ex.Message}");
                    if (TryReattachToTarget(ex.Message))
                    {
                        continue;
                    }

                    return ReportIncompleteTransfer();
                }
                catch (IOException ex)
                {
                    LibraryLogger.Warning($"Sahara transport error while waiting for the target: {ex.Message}");
                    if (TryFinishAfterProgrammer($"dropped off the bus ({ex.Message})"))
                    {
                        return true;
                    }

                    if (TryReattachToTarget(ex.Message))
                    {
                        continue;
                    }

                    return ReportIncompleteTransfer();
                }

                if (packet.AsSpan().StartsWith("<?xml"u8))
                {
                    LibraryLogger.Debug(
                        $"Received Firehose XML; the programmer is running. Served: [{FormatImageIds(_servedImageIds)}].");
                    return true;
                }

                var outcome = HandlePacket(packet);
                if (outcome.HasValue)
                {
                    return outcome.Value;
                }
            }
        }
        catch (Exception ex)
        {
            LibraryLogger.Error($"Multi-image transfer crashed: {ex.Message}");
            LibraryLogger.Debug(ex.ToString());
            return false;
        }
        finally
        {
            _transport.TimeoutMilliseconds = originalTimeout;
        }
    }

    private bool? HandlePacket(byte[] packet)
    {
        LibraryLogger.Trace($"Received Sahara packet ({packet.Length} bytes): {Convert.ToHexString(packet)}");
        if (packet.Length < 0x08)
        {
            LibraryLogger.Error(
                $"Sahara packet is only {packet.Length} bytes: {Convert.ToHexString(packet)}");
            return ReportProtocolError();
        }

        var command = (QualcommSaharaCommand)ByteOperations.ReadUInt32(packet, 0x00);
        var declaredLength = ByteOperations.ReadUInt32(packet, 0x04);
        LibraryLogger.Debug(
            $"Received Sahara {command} (0x{(uint)command:X8}), packet length {packet.Length}, declared length {declaredLength}.");
        if (declaredLength != packet.Length)
        {
            LibraryLogger.Error(
                $"Sahara {command} length mismatch: received {packet.Length} bytes, header declares {declaredLength}. Raw packet: {Convert.ToHexString(packet)}");
            return ReportProtocolError();
        }

        switch (command)
        {
            case QualcommSaharaCommand.Hello:
                return HandleHello(packet);

            case QualcommSaharaCommand.ReadData:
                return ServeImageChunk(
                    ByteOperations.ReadUInt32(packet, 0x08),
                    ByteOperations.ReadUInt32(packet, 0x0C),
                    ByteOperations.ReadUInt32(packet, 0x10),
                    packet,
                    0x14,
                    "32-bit");

            case QualcommSaharaCommand.ReadData64Bit:
                return ServeImageChunk(
                    (uint)ByteOperations.ReadUInt64(packet, 0x08),
                    ByteOperations.ReadUInt64(packet, 0x10),
                    ByteOperations.ReadUInt64(packet, 0x18),
                    packet,
                    0x20,
                    "64-bit");

            case QualcommSaharaCommand.EndImageTx:
                return HandleEndImageTx(packet);

            case QualcommSaharaCommand.DoneResponse:
                return HandleDoneResponse(packet);

            case QualcommSaharaCommand.Done:
                LibraryLogger.Error(
                    $"Target sent a Sahara DONE request; the host is the side that sends DONE. Raw packet: {Convert.ToHexString(packet)}");
                return ReportProtocolError();

            case QualcommSaharaCommand.NoCommand:
            case QualcommSaharaCommand.HelloResponse:
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
                if (++_unexpectedPacketCount <= MaxUnexpectedPackets)
                {
                    LibraryLogger.Warning(
                        $"Skipping unexpected Sahara command 0x{(uint)command:X8} ({_unexpectedPacketCount}/{MaxUnexpectedPackets}). Raw packet: {Convert.ToHexString(packet)}");
                    return null;
                }

                LibraryLogger.Error(
                    $"Received {_unexpectedPacketCount} unexpected Sahara commands; last was 0x{(uint)command:X8}. Raw packet: {Convert.ToHexString(packet)}");
                return ReportProtocolError();
        }
    }

    private bool? HandleHello(byte[] packet)
    {
        if (packet.Length < HelloPacketLength)
        {
            LibraryLogger.Error(
                $"Sahara HELLO is only {packet.Length} bytes, expected {HelloPacketLength}. Raw packet: {Convert.ToHexString(packet)}");
            return ReportProtocolError();
        }

        var version = ByteOperations.ReadUInt32(packet, 0x08);
        var minimumVersion = ByteOperations.ReadUInt32(packet, 0x0C);
        var maxCommandLength = ByteOperations.ReadUInt32(packet, 0x10);
        var mode = ByteOperations.ReadUInt32(packet, 0x14);
        LibraryLogger.Debug(
            $"Stage HELLO: version {version}, minimum version {minimumVersion}, max command length 0x{maxCommandLength:X}, mode {DescribeMode(mode)}. Raw packet: {Convert.ToHexString(packet)}");

        if (_negotiatedVersion != version && _servedImageIds.Count > 0)
        {
            LibraryLogger.Debug(
                $"Stage protocol version changed from {_negotiatedVersion} to {version}.");
        }

        _negotiatedVersion = version;
        if (mode != ImageTxPending)
        {
            LibraryLogger.Warning(
                $"Stage HELLO mode is {DescribeMode(mode)}, not image transfer pending; echoing it back.");
        }

        SendHelloResponse(version, mode);
        return null;
    }

    private bool? ServeImageChunk(uint imageId, ulong offset, ulong length, byte[] packet,
        int expectedPacketLength, string readWidth)
    {
        if (packet.Length < expectedPacketLength)
        {
            LibraryLogger.Error(
                $"Sahara {readWidth} READ_DATA is {packet.Length} bytes, expected at least {expectedPacketLength}. Raw packet: {Convert.ToHexString(packet)}");
            return ReportProtocolError();
        }

        if (!imageMappings.TryGetValue(imageId, out var filePath))
        {
            LibraryLogger.Error(
                $"Target requested image id {imageId}, which the Sahara config does not map. Mapped ids: [{FormatImageIds(imageMappings.Keys.OrderBy(id => id))}].");
            return ReportProtocolError();
        }

        try
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            LibraryLogger.Debug(
                $"Requested ID: {imageId}, offset 0x{offset:X}, length 0x{length:X} ({readWidth} read), Path: {filePath}");
            if (offset + length > (ulong)fileStream.Length)
            {
                LibraryLogger.Error(
                    $"Target asked for offset 0x{offset:X} length 0x{length:X} of image id {imageId}, but {filePath} is only 0x{fileStream.Length:X} bytes.");
                return ReportProtocolError();
            }

            var buffer = new byte[length];
            _ = fileStream.Seek((long)offset, SeekOrigin.Begin);
            fileStream.ReadExactly(buffer, 0, (int)length);
            _transport.SendData(buffer);
            return null;
        }
        catch (IOException ex)
        {
            LibraryLogger.Error(
                $"Failed to serve image id {imageId} from {filePath}: {ex.Message}");
            return ReportProtocolError();
        }
    }

    private bool? HandleEndImageTx(byte[] packet)
    {
        if (packet.Length < EndImageTxPacketLength)
        {
            LibraryLogger.Error(
                $"Sahara END_IMAGE_TX is {packet.Length} bytes, expected at least {EndImageTxPacketLength}. Raw packet: {Convert.ToHexString(packet)}");
            return ReportProtocolError();
        }

        var imageId = ByteOperations.ReadUInt32(packet, 0x08);
        var status = ByteOperations.ReadUInt32(packet, 0x0C);
        LibraryLogger.Debug(
            $"Received END_IMAGE_TX: Image ID {imageId}, Status {DescribeStatus(status)}. Raw packet: {Convert.ToHexString(packet)}");
        if (status != (uint)QualcommSaharaStatusCode.StatusSuccess)
        {
            LibraryLogger.Error(
                $"Target rejected image id {imageId}: {DescribeStatus(status)}. DONE will not be sent.");
            return ReportProtocolError();
        }

        if (_servedImageIds.Contains(imageId))
        {
            LibraryLogger.Warning(
                $"Image id {imageId} was already served in this session{(_reconnectedAtLeastOnce ? " before the re-attach" : "")}.");
        }

        _servedImageIds.Add(imageId);
        LibraryLogger.Debug($"Image ID {imageId} transfer succeeded, sending DONE.");
        _transport.SendData(QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.Done));
        return null;
    }

    private bool? HandleDoneResponse(byte[] packet)
    {
        if (packet.Length < DoneResponsePacketLength)
        {
            LibraryLogger.Error(
                $"Sahara DONE_RESP is {packet.Length} bytes, expected at least {DoneResponsePacketLength}. Raw packet: {Convert.ToHexString(packet)}");
            return ReportProtocolError();
        }

        var status = ByteOperations.ReadUInt32(packet, 0x08);
        LibraryLogger.Debug(
            $"Received DONE_RESP: {DescribeImageTxStatus(status)}. Raw packet: {Convert.ToHexString(packet)}");

        if (_servedImageIds.Count > 0 && _servedImageIds[^1] == FirehoseProgrammerImageId)
        {
            LibraryLogger.Debug(
                $"Image id {FirehoseProgrammerImageId} transferred; leaving the Sahara protocol.");
            return true;
        }

        switch (status)
        {
            case ImageTxComplete:
                LibraryLogger.Debug(
                    $"IMAGE_TX_COMPLETE after image ids [{FormatImageIds(_servedImageIds)}].");
                return true;

            case ImageTxPending:
                LibraryLogger.Debug(
                    $"IMAGE_TX_PENDING; waiting up to {StageTimeoutMilliseconds} ms for the next stage HELLO.");
                return null;

            default:
                LibraryLogger.Error($"Target returned an invalid DONE_RESP status 0x{status:X8}.");
                return ReportProtocolError();
        }
    }

    private void SendHelloResponse(uint protocolVersion, uint mode)
    {
        var payload = new byte[0x28];
        ByteOperations.WriteUInt32(payload, 0x00, protocolVersion);
        ByteOperations.WriteUInt32(payload, 0x04, protocolVersion);
        ByteOperations.WriteUInt32(payload, 0x08, (uint)QualcommSaharaStatusCode.StatusSuccess);
        ByteOperations.WriteUInt32(payload, 0x0C, mode);
        for (uint field = 0; field < 6; field++)
        {
            ByteOperations.WriteUInt32(payload, 0x10 + (field * 4), field + 1);
        }

        var helloResponse = QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.HelloResponse, payload);
        LibraryLogger.Debug(
            $"Sending HELLO_RESP with version {protocolVersion}, mode {DescribeMode(mode)}: {Convert.ToHexString(helloResponse)}");
        _transport.SendData(helloResponse);
    }

    private bool TrySpeculativeHelloResponse()
    {
        if (_speculativeHelloSent || _servedImageIds.Count > 0 ||
            _transport.Backend != TransportBackend.WindowsQud)
        {
            return false;
        }

        LibraryLogger.Warning(
            "Initial Sahara read timed out on Windows QUD; sending a speculative HELLO response for image transfer mode.");
        _speculativeHelloSent = true;
        SendHelloResponse(FallbackProtocolVersion, ImageTxPending);
        return true;
    }

    private bool TryReattachToTarget(string reason)
    {
        if (transportReconnector is null)
        {
            LibraryLogger.Debug("No transport reconnector was supplied; cannot re-attach to the target.");
            return false;
        }

        if (_reconnectCount >= MaxReconnectAttempts)
        {
            LibraryLogger.Error(
                $"Target went silent again ({reason}) after {_reconnectCount} re-attach attempt(s); giving up.");
            return false;
        }

        _reconnectCount++;
        LibraryLogger.Warning(
            $"Target went silent ({reason}) after serving image ids [{FormatImageIds(_servedImageIds)}]. Re-opening the EDL device (attempt {_reconnectCount}/{MaxReconnectAttempts}).");

        for (var poll = 0; poll < ReconnectPollAttempts; poll++)
        {
            Thread.Sleep(ReconnectPollIntervalMilliseconds);
            IQualcommTransport? reopened;
            try
            {
                reopened = transportReconnector();
            }
            catch (Exception ex)
            {
                LibraryLogger.Debug($"Re-attach attempt failed: {ex.Message}");
                continue;
            }

            if (reopened is null)
            {
                continue;
            }

            _transport = reopened;
            _reconnectedAtLeastOnce = true;
            LibraryLogger.Warning(
                $"Re-attached after {(poll + 1) * ReconnectPollIntervalMilliseconds} ms; resuming.");
            ClearTargetStateMachine();
            return true;
        }

        LibraryLogger.Error(
            $"The EDL device did not come back within {ReconnectPollAttempts * ReconnectPollIntervalMilliseconds} ms.");
        return false;
    }

    private void ClearTargetStateMachine()
    {
        try
        {
            var packet = QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.ResetStateMachine);
            LibraryLogger.Warning(
                $"Sending RESET_STATE_MACHINE: {Convert.ToHexString(packet)}");
            _transport.SendData(packet);
        }
        catch (Exception ex)
        {
            LibraryLogger.Warning(
                $"RESET_STATE_MACHINE write failed: {ex.Message}");
        }
    }

    private bool IsChainFinished()
    {
        return _expectsProgrammerImage
            ? _servedImageIds.Contains(FirehoseProgrammerImageId)
            : _servedImageIds.Count > 0;
    }

    private bool TryFinishAfterProgrammer(string reason)
    {
        if (_expectsProgrammerImage && !_servedImageIds.Contains(FirehoseProgrammerImageId))
        {
            return false;
        }

        if (_servedImageIds.Count == 0)
        {
            return false;
        }

        LibraryLogger.Debug(
            $"Programmer loaded and target {reason}; Sahara chain complete. Served: [{FormatImageIds(_servedImageIds)}].");
        ActivateProgrammer();
        return true;
    }

    private void ActivateProgrammer()
    {
        try
        {
            var done = QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.Done);
            LibraryLogger.Debug($"Sending final DONE to start the programmer: {Convert.ToHexString(done)}");
            _transport.TimeoutMilliseconds = PostProgrammerTimeoutMilliseconds;
            _transport.SendData(done);
            var response = _transport.GetResponse(null);
            LibraryLogger.Debug($"Response to the final DONE: {Convert.ToHexString(response)}");
        }
        catch (Exception ex)
        {
            LibraryLogger.Debug(
                $"No answer to the final DONE: {ex.Message}");
        }
    }

    private bool ReportProtocolError()
    {
        try
        {
            var reset = QualcommSahara.BuildCommandPacket(QualcommSaharaCommand.Reset);
            LibraryLogger.Debug($"Sending RESET after protocol error: {Convert.ToHexString(reset)}");
            _transport.SendData(reset);
        }
        catch (Exception ex)
        {
            LibraryLogger.Debug($"RESET could not be sent: {ex.Message}");
        }

        return ReportIncompleteTransfer();
    }

    private bool ReportIncompleteTransfer()
    {
        LibraryLogger.Error(
            "Multi-image transfer did not finish: no IMAGE_TX_COMPLETE and no Firehose.");
        LibraryLogger.Error($"Image ids served, in order: [{FormatImageIds(_servedImageIds)}].");
        LibraryLogger.Error(
            $"Image ids never requested: [{FormatImageIds(imageMappings.Keys.Where(id => !_servedImageIds.Contains(id)).OrderBy(id => id))}].");
        if (_reconnectedAtLeastOnce)
        {
            LibraryLogger.Error(
                $"Target stayed on the bus but answered nothing after {_reconnectCount} re-attach attempt(s).");
        }

        return false;
    }

    private static string FormatImageIds(IEnumerable<uint> imageIds)
    {
        return string.Join(", ", imageIds.Select(id => id.ToString(CultureInfo.InvariantCulture)));
    }

    private static string DescribeMode(uint mode)
    {
        var saharaMode = (QualcommSaharaMode)mode;
        return Enum.IsDefined(saharaMode) ? $"{saharaMode} (0x{mode:X2})" : $"Unknown (0x{mode:X8})";
    }

    private static string DescribeStatus(uint status)
    {
        var statusCode = (QualcommSaharaStatusCode)status;
        return Enum.IsDefined(statusCode) ? $"{statusCode} (0x{status:X8})" : $"Unknown (0x{status:X8})";
    }

    private static string DescribeImageTxStatus(uint status)
    {
        return status switch
        {
            ImageTxPending => "IMAGE_TX_PENDING (0x00000000)",
            ImageTxComplete => "IMAGE_TX_COMPLETE (0x00000001)",
            _ => $"Unknown (0x{status:X8})"
        };
    }
}
