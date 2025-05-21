using System.Text;
using System.Text.Json;
using System.Xml.Serialization;
using System.Xml;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;
using System.Diagnostics;
using QCEDL.NET.Logging;
using QCEDL.NET.Json;
using Qualcomm.EmergencyDownload.Transport;

namespace Qualcomm.EmergencyDownload.Layers.APSS.Firehose
{
    public static class QualcommFirehoseCommands
    {
        public static bool Configure(this QualcommFirehose Firehose, StorageType storageType, bool skipStorageInit = false)
        {
            LibraryLogger.Debug($"Configuring (Memory: {storageType}, SkipStorageInit: {skipStorageInit})");

            string Command03 = QualcommFirehoseXml.BuildCommandPacket([
                QualcommFirehoseXmlPackets.GetConfigurePacket(storageType, true, 1048576, false, 8192, true, false, skipStorageInit)
            ]);

            Firehose.Serial.SendData(Encoding.UTF8.GetBytes(Command03));

            bool GotResponse = false;

            while (!GotResponse)
            {
                Data[] datas = Firehose.GetFirehoseResponseDataPayloads();

                foreach (Data data in datas)
                {
                    if (data.Log != null)
                    {
                        LibraryLogger.Debug("DEVPRG LOG: " + data.Log.Value);
                    }
                    else if (data.Response != null)
                    {
                        if (data.Response.Value == "ACK")
                        {
                            LibraryLogger.Debug("Configure command ACKed.");
                        }
                        else if (data.Response.Value == "Nak")
                        {
                            LibraryLogger.Error("Configure command NAKed.");
                            return false;
                        }

                        GotResponse = true;
                    }
                    else
                    {
                        LibraryLogger.Warning("Received unexpected data payload during Configure.");
                    }
                    if (datas.Length == 0 && !GotResponse)
                    {
                        LibraryLogger.Error("No response received for Configure command.");
                        return false;
                    }
                }
            }
            return true;
        }

        public static byte[] GetExpectedBufferLength(this QualcommFirehose Firehose, int length)
        {
            List<byte> bufferList = [];
            int bytesReadSoFar = 0; // Track bytes read
            int maxSingleRead = 1048576; // Read in chunks up to 1MB, adjust as needed
            do
            {
                int remaining = length - bytesReadSoFar;
                int currentReadLength = Math.Min(remaining, maxSingleRead);
                if (currentReadLength <= 0) break; // Should not happen if length > 0
                // Call GetResponse with the specific amount we want for this chunk
                byte[] chunk = Firehose.Serial.GetResponse(null, Length: currentReadLength);

                if (chunk == null || chunk.Length == 0)
                {
                    LibraryLogger.Warning($"GetResponse returned 0 or null chunk while expecting {currentReadLength} bytes. Read {bytesReadSoFar}/{length} so far.");
                    throw new BadConnectionException("Failed to read expected data chunk from device.");
                }

                bufferList.AddRange(chunk);
                bytesReadSoFar += chunk.Length;
            } while (bytesReadSoFar < length);
            if (bytesReadSoFar != length)
            {
                LibraryLogger.Error($"Failed to read the complete expected buffer. Expected {length}, got {bytesReadSoFar}.");
                throw new BadMessageException($"Expected {length} bytes but received {bytesReadSoFar}.");
            }
            byte[] ResponseBuffer = [.. bufferList];
            return ResponseBuffer;
        }

        public static byte[] Read(this QualcommFirehose Firehose, StorageType storageType, uint LUNi, uint sectorSize, uint FirstSector, uint LastSector)
        {
            LibraryLogger.Debug($"READ: LUN{LUNi}, FirstSector: {FirstSector}, LastSector: {LastSector}, SectorSize: {sectorSize}");

            string Command03 = QualcommFirehoseXml.BuildCommandPacket([
                QualcommFirehoseXmlPackets.GetReadPacket(storageType, LUNi, sectorSize, FirstSector, LastSector)
            ]);

            Firehose.Serial.SendData(Encoding.UTF8.GetBytes(Command03));

            bool RawMode = false;
            bool GotResponse = false;

            while (!GotResponse)
            {
                // Data[] datas = Firehose.GetFirehoseResponseDataPayloads(true);
                Data[] datas = Firehose.GetFirehoseResponseDataPayloads(); // WaitTilFooter = false (default)

                foreach (Data data in datas)
                {
                    if (data.Log != null)
                    {
                        LibraryLogger.Debug("DEVPRG LOG: " + data.Log.Value);
                    }
                    else if (data.Response != null)
                    {
                        if (data.Response.Value == "ACK")
                        {
                            if (data.Response.RawMode)
                            {
                                RawMode = true;
                                LibraryLogger.Debug("Read command ACKed, raw mode enabled.");
                            }
                            else
                            {
                                LibraryLogger.Warning("Read command ACKed, but raw mode not indicated. Proceeding cautiously.");
                            }
                            GotResponse = true;
                        }
                        else if (data.Response.Value == "Nak")
                        {
                            LibraryLogger.Error($"Read command NAKed. Message: {data.Response.Value}"); // Assuming NAK might have more info
                            return null;
                        }
                        else if (!string.IsNullOrEmpty(data.Response.Value))
                        {
                            LibraryLogger.Warning($"Unexpected response value: {data.Response.Value} while waiting for raw mode ACK.");
                        }
                    }
                    else
                    {
                        LibraryLogger.Warning("Why are we here?");
                    }
                    if (GotResponse) break; // Break inner loop if response found
                }
                if (datas.Length == 0 && !GotResponse) // Safety break if GetFirehoseResponseDataPayloads returns empty without setting GotResponse
                {
                    LibraryLogger.Error("Received empty data payload from GetFirehoseResponseDataPayloads (Read rawmode ACK loop), breaking.");
                    return null;
                }
            }

            if (!RawMode)
            {
                LibraryLogger.Error("Error: Raw mode not enabled");
                return null;
            }

            uint numSectorsToRead = LastSector - FirstSector + 1;
            int totalReadLength = (int)(numSectorsToRead * sectorSize);
            if (totalReadLength <= 0)
            {
                LibraryLogger.Warning($"Calculated totalReadLength is {totalReadLength}. Returning empty array.");
                return [];
            }

            byte[] readBuffer = Firehose.GetExpectedBufferLength(totalReadLength);


            // LOOP 2: Getting final ACK
            GotResponse = false; // Reset for the final ACK
            int finalAckAttempts = 0;
            while (!GotResponse && finalAckAttempts < 5) // Add attempt limit
            {
                finalAckAttempts++;
                Data[] datas = Firehose.GetFirehoseResponseDataPayloads(); // WaitTilFooter = false (default)
                foreach (Data data in datas)
                {
                    if (data.Log != null)
                    {
                        LibraryLogger.Debug("DEVPRG LOG: " + data.Log.Value);
                    }
                    else if (data.Response != null)
                    {
                        if (data.Response.Value == "ACK")
                        {
                            LibraryLogger.Debug("Final ACK received for Read operation.");
                            GotResponse = true;
                        }
                        else if (data.Response.Value == "Nak")
                        {
                            LibraryLogger.Error($"Final NAK received for Read operation. Message: {data.Response.Value}");
                            return null;
                        }
                        else if (!string.IsNullOrEmpty(data.Response.Value))
                        {
                            LibraryLogger.Warning($"Unexpected response value: {data.Response.Value} while waiting for final ACK for Read.");
                        }
                    }
                    else
                    {
                        LibraryLogger.Warning("Why are we here?");
                    }
                    if (GotResponse) break;
                }
                if (datas.Length == 0 && !GotResponse)
                {
                    LibraryLogger.Warning($"Received empty data payload from GetFirehoseResponseDataPayloads (final ACK loop attempt {finalAckAttempts}), breaking.");
                    // Consider if a short delay is needed here if device is slow to send final ACK
                    System.Threading.Thread.Sleep(50);
                }
            }
            if (!GotResponse)
            {
                LibraryLogger.Warning("Did not receive a clear final ACK/NAK after data transfer.");
            }

            return readBuffer;
        }

        public static bool ReadToStream(this QualcommFirehose Firehose, StorageType storageType, uint LUNi, uint sectorSize,
                                        uint FirstSector, uint LastSector, Stream outputStream, Action<long, long>? progressCallback = null)
        {
            LibraryLogger.Debug($"ReadToStream: LUN{LUNi}, FirstSector: {FirstSector}, LastSector: {LastSector}, SectorSize: {sectorSize}");

            string Command03 = QualcommFirehoseXml.BuildCommandPacket([
                QualcommFirehoseXmlPackets.GetReadPacket(storageType, LUNi, sectorSize, FirstSector, LastSector)
            ]);

            Firehose.Serial.SendData(Encoding.UTF8.GetBytes(Command03));

            bool RawMode = false;
            bool GotResponse = false;

            while (!GotResponse)
            {
                Data[] datas = Firehose.GetFirehoseResponseDataPayloads();
                foreach (Data data in datas)
                {
                    if (data.Log != null)
                    {
                        LibraryLogger.Debug("DEVPRG LOG: " + data.Log.Value);
                    }
                    else if (data.Response != null)
                    {
                        if (data.Response.Value == "ACK")
                        {
                            if (data.Response.RawMode)
                            {
                                RawMode = true;
                                LibraryLogger.Debug("Read command ACKed, raw mode enabled.");
                            }
                            else
                            {
                                LibraryLogger.Warning("Read command ACKed, but raw mode not indicated. Proceeding cautiously.");
                            }
                            GotResponse = true;
                        }
                        else if (data.Response.Value == "Nak")
                        {
                            LibraryLogger.Error($"Read command NAKed. Message: {data.Response.Value}"); // Assuming NAK might have more info
                            return false;
                        }
                        else if (!string.IsNullOrEmpty(data.Response.Value))
                        {
                            LibraryLogger.Warning($"Unexpected response value: {data.Response.Value} while waiting for raw mode ACK.");
                        }
                    }
                    else
                    {
                        LibraryLogger.Warning("Why are we here?");
                    }
                    if (GotResponse) break; // Break inner loop if response found
                }
                if (datas.Length == 0 && !GotResponse) // Safety break if GetFirehoseResponseDataPayloads returns empty without setting GotResponse
                {
                    LibraryLogger.Error("Received empty data payload from GetFirehoseResponseDataPayloads (Read rawmode ACK loop), breaking.");
                    return false;
                }
            }

            if (!RawMode)
            {
                LibraryLogger.Error("Error: Raw mode not enabled");
                return false;
            }

            long numSectorsToRead = (long)LastSector - FirstSector + 1;
            long totalReadLength = numSectorsToRead * sectorSize;

            if (totalReadLength <= 0)
            {
                LibraryLogger.Warning($"Calculated totalReadLength is {totalReadLength}. Returning empty array.");
                return false;
            }

            bool readSuccess = Firehose.ReadAndWriteChunksToStream(totalReadLength, outputStream, progressCallback);

            // LOOP 2: Getting final ACK
            GotResponse = false; // Reset for the final ACK
            int finalAckAttempts = 0;
            while (!GotResponse && finalAckAttempts < 5) // Add attempt limit
            {
                finalAckAttempts++;
                Data[] datas = Firehose.GetFirehoseResponseDataPayloads(); // WaitTilFooter = false (default)
                foreach (Data data in datas)
                {
                    if (data.Log != null)
                    {
                        LibraryLogger.Debug("DEVPRG LOG: " + data.Log.Value);
                    }
                    else if (data.Response != null)
                    {
                        if (data.Response.Value == "ACK")
                        {
                            LibraryLogger.Debug("Final ACK received for Read operation.");
                            GotResponse = true;
                        }
                        else if (data.Response.Value == "Nak")
                        {
                            LibraryLogger.Error($"Final NAK received for Read operation. Message: {data.Response.Value}");
                            return false;
                        }
                        else if (!string.IsNullOrEmpty(data.Response.Value))
                        {
                            LibraryLogger.Warning($"Unexpected response value: {data.Response.Value} while waiting for final ACK for Read.");
                        }
                    }
                    else
                    {
                        LibraryLogger.Warning("Why are we here?");
                    }
                    if (GotResponse) break;
                }
                if (datas.Length == 0 && !GotResponse)
                {
                    LibraryLogger.Warning($"Received empty data payload from GetFirehoseResponseDataPayloads (final ACK loop attempt {finalAckAttempts}), breaking.");
                    // Consider if a short delay is needed here if device is slow to send final ACK
                    Thread.Sleep(50);
                }
            }
            if (!GotResponse)
            {
                LibraryLogger.Warning("Did not receive a clear final ACK/NAK after data transfer.");
            }

            return true;
        }

        internal static bool ReadAndWriteChunksToStream(this QualcommFirehose Firehose, long totalLength, Stream outputStream, Action<long, long>? progressCallback = null)
        {
            long bytesReadSoFar = 0;
            int readChunkSize;

            if (Firehose.Serial.ActiveCommunicationMode == CommunicationMode.SerialPort)
            {
                readChunkSize = 1024 * 32; // 32KB
            }
            else
            {
                readChunkSize = 1024 * 1024; // 1MB
            }

            Stopwatch sw = Stopwatch.StartNew();

            while (bytesReadSoFar < totalLength)
            {
                int currentChunkToRequest = (int)Math.Min(readChunkSize, totalLength - bytesReadSoFar);

                // GetResponse will read up to currentChunkToRequest or timeout
                byte[] actualChunkRead;
                try
                {
                    // QualcommSerial.GetResponse reads what's available up to Length or times out.
                    // It doesn't guarantee filling the buffer if less data arrives before timeout.
                    actualChunkRead = Firehose.Serial.GetResponse(null, Length: currentChunkToRequest);
                }
                catch (TimeoutException)
                {
                    LibraryLogger.Error($"Timeout while reading data chunk. Read {bytesReadSoFar}/{totalLength} bytes.");
                    return false;
                }
                catch (BadConnectionException bce)
                {
                    LibraryLogger.Error($"Bad connection while reading data chunk: {bce.Message}. Read {bytesReadSoFar}/{totalLength} bytes.");
                    return false;
                }
                catch (Exception ex)
                {
                    LibraryLogger.Error($"Generic error while reading data chunk: {ex.Message}. Read {bytesReadSoFar}/{totalLength} bytes.");
                    return false;
                }

                if (actualChunkRead == null || actualChunkRead.Length == 0)
                {
                    // This might happen if the device stops sending data prematurely or if GetResponse timed out
                    // but didn't throw TimeoutException (depends on its internal logic).
                    LibraryLogger.Warning($"GetResponse returned null or empty chunk. Read {bytesReadSoFar}/{totalLength} bytes. Assuming end of data or error.");
                    // If we haven't read everything, this is an error.
                    return bytesReadSoFar == totalLength;
                }

                try
                {
                    outputStream.Write(actualChunkRead, 0, actualChunkRead.Length);
                }
                catch (IOException ioEx)
                {
                    LibraryLogger.Error($"IO Error writing chunk to output stream: {ioEx.Message}");
                    return false;
                }

                bytesReadSoFar += actualChunkRead.Length;
                progressCallback?.Invoke(bytesReadSoFar, totalLength);

                // If GetResponse consistently returns less than requested even if more is available,
                // this loop will still work, just with smaller effective chunks.
                // If the device sends less than `totalLength` in total, `bytesReadSoFar` will not equal `totalLength`.
            }
            sw.Stop();

            if (bytesReadSoFar != totalLength)
            {
                LibraryLogger.Error($"Failed to read the complete expected buffer. Expected {totalLength}, got {bytesReadSoFar}.");
                return false;
            }

            LibraryLogger.Debug($"Successfully read and wrote {bytesReadSoFar} bytes in {sw.ElapsedMilliseconds} ms.");
            return true;
        }

        public static bool Program(this QualcommFirehose Firehose, StorageType storageType, uint LUNi, uint sectorSize, uint startSector, string? filenameForXml, byte[] dataToWrite)
        {
            if (dataToWrite == null || dataToWrite.Length == 0)
            {
                LibraryLogger.Warning("Program command: No data to write.");
                return false;
            }
            if (dataToWrite.Length % sectorSize != 0)
            {
                LibraryLogger.Error($"Program command: Data length ({dataToWrite.Length}) is not a multiple of sector size ({sectorSize}).");
                return false;
            }
            uint numSectorsToWrite = (uint)(dataToWrite.Length / sectorSize);
            LibraryLogger.Debug($"PROGRAM: LUN{LUNi}, StartSector: {startSector}, NumSectors: {numSectorsToWrite}, SectorSize: {sectorSize}, File: {filenameForXml ?? "N/A"}");
            string programCommandXml = QualcommFirehoseXml.BuildCommandPacket([
                QualcommFirehoseXmlPackets.GetProgramPacket(storageType, LUNi, sectorSize, startSector, numSectorsToWrite, filenameForXml)
            ]);
            Firehose.Serial.SendData(Encoding.UTF8.GetBytes(programCommandXml));
            bool rawModeEnabled = false;
            bool initialResponseReceived = false;
            // LOOP 1: Wait for rawmode="true" ACK
            while (!initialResponseReceived)
            {
                // Data[] datas = Firehose.GetFirehoseResponseDataPayloads(true);
                Data[] datas = Firehose.GetFirehoseResponseDataPayloads(); // WaitTilFooter = false (default)
                foreach (Data dataElement in datas)
                {
                    if (dataElement.Log != null)
                    {
                        LibraryLogger.Debug("DEVPRG LOG: " + dataElement.Log.Value);
                    }
                    else if (dataElement.Response != null)
                    {
                        if (dataElement.Response.Value == "ACK")
                        {
                            if (dataElement.Response.RawMode)
                            {
                                rawModeEnabled = true;
                                LibraryLogger.Debug("Program command ACKed, raw mode enabled.");
                            }
                            else
                            {
                                LibraryLogger.Warning("Program command ACKed, but raw mode NOT indicated. This is unexpected.");
                            }
                            initialResponseReceived = true;
                        }
                        else if (dataElement.Response.Value == "Nak")
                        {
                            LibraryLogger.Error($"Program command NAKed. Message: {dataElement.Response.Value}");
                            return false;
                        }
                        else if (!string.IsNullOrEmpty(dataElement.Response.Value))
                        {
                            LibraryLogger.Warning($"Unexpected response value: {dataElement.Response.Value} while waiting for Program raw mode ACK.");
                        }
                    }
                    else
                    {
                        LibraryLogger.Warning("Why are we here?");
                    }
                    if (initialResponseReceived) break;
                }
                if (datas.Length == 0 && !initialResponseReceived)
                {
                    LibraryLogger.Warning("Received empty data payload from GetFirehoseResponseDataPayloads (Program rawmode ACK loop), breaking.");
                    return false;
                }
            }
            if (!rawModeEnabled)
            {
                LibraryLogger.Error("Raw mode was not enabled by the device for Program operation. Aborting write.");
                return false;
            }
            // Send the actual data
            LibraryLogger.Debug($"Sending {dataToWrite.Length} bytes of raw data...");
            try
            {
                if (storageType == StorageType.SPINOR)
                {
                    // For SPINOR, we just use a long enough timeout
                    LibraryLogger.Debug($"Setting Firehose serial timeout to 300 s for SPINOR.");
                    Firehose.Serial.SetTimeOut(300000);
                }
                else
                {
                    // For other storage types, we can use the default timeout
                    LibraryLogger.Trace($"Using default Firehose serial timeout for {storageType}.");
                    Firehose.Serial.SetTimeOut(10000);
                }
                Firehose.Serial.SendLargeRawData(dataToWrite);
            }
            catch (Exception ex)
            {
                LibraryLogger.Error($"Error sending raw data for Program command: {ex.Message}");
                return false;
            }
            LibraryLogger.Debug("Raw data sent.");
            Firehose.Serial.SendZeroLengthPacket();

            // LOOP 2: Wait for final ACK/NAK after data transfer
            bool finalAckReceived = false;
            int finalAckAttempts = 0;
            while (!finalAckReceived && finalAckAttempts < 10) // Increased attempts for potentially slow writes
            {
                finalAckAttempts++;
                Data[] datas = Firehose.GetFirehoseResponseDataPayloads(); // waitTilFooter = false (default)
                foreach (Data dataElement in datas)
                {
                    if (dataElement.Log != null)
                    {
                        LibraryLogger.Debug("DEVPRG LOG: " + dataElement.Log.Value);
                    }
                    else if (dataElement.Response != null)
                    {
                        if (dataElement.Response.Value == "ACK")
                        {
                            LibraryLogger.Debug("Program operation successful (final ACK received).");
                            finalAckReceived = true;
                        }
                        else if (dataElement.Response.Value == "Nak")
                        {
                            LibraryLogger.Error($"Program operation failed (final NAK received). Message: {dataElement.Response.Value}");
                            return false;
                        }
                        else if (!string.IsNullOrEmpty(dataElement.Response.Value))
                        {
                            LibraryLogger.Warning($"Unexpected response value: {dataElement.Response.Value} while waiting for final Program ACK.");
                        }
                    }
                    else
                    {
                        LibraryLogger.Warning("Why are we here?");
                    }
                    if (finalAckReceived) break;
                }
                if (datas.Length == 0 && !finalAckReceived)
                {
                    LibraryLogger.Warning($"Received empty data payload (Program final ACK loop attempt {finalAckAttempts}). Waiting briefly...");
                    System.Threading.Thread.Sleep(200);
                }
            }
            if (!finalAckReceived)
            {
                LibraryLogger.Error("Did not receive a clear final ACK/NAK after Program data transfer.");
                return false;
            }
            return true;
        }

        public static bool ProgramFromStream(this QualcommFirehose Firehose,
                                                 StorageType storageType,
                                                 uint LUNi,
                                                 uint sectorSize,
                                                 uint startSector,
                                                 uint numSectorsForXml, // Number of sectors based on total padded size
                                                 long totalBytesToStreamIncludingPadding,
                                                 string? filenameForXml,
                                                 Stream inputStream,
                                                 Action<long, long>? progressCallback = null)
        {
            LibraryLogger.Debug($"PROGRAM (from stream): LUN{LUNi}, StartSector: {startSector}, NumSectorsInXml: {numSectorsForXml}, TotalBytesToStream: {totalBytesToStreamIncludingPadding}, SectorSize: {sectorSize}, File: {filenameForXml ?? "N/A"}");

            if (totalBytesToStreamIncludingPadding == 0)
            {
                LibraryLogger.Warning("ProgramFromStream: totalBytesToStreamIncludingPadding is 0. Nothing to write.");
                return true;
            }
            if (totalBytesToStreamIncludingPadding % sectorSize != 0)
            {
                LibraryLogger.Error($"ProgramFromStream: totalBytesToStreamIncludingPadding ({totalBytesToStreamIncludingPadding}) is not a multiple of sectorSize ({sectorSize}). This is a logic error.");
                return false;
            }

            string programCommandXml = QualcommFirehoseXml.BuildCommandPacket([
                QualcommFirehoseXmlPackets.GetProgramPacket(storageType, LUNi, sectorSize, startSector, numSectorsForXml, filenameForXml)
            ]);

            Firehose.Serial.SendData(Encoding.UTF8.GetBytes(programCommandXml));

            bool rawModeEnabled = false;
            bool initialResponseReceived = false;

            // LOOP 1: Wait for rawmode="true" ACK
            while (!initialResponseReceived)
            {
                Data[] datas = Firehose.GetFirehoseResponseDataPayloads();
                foreach (Data dataElement in datas)
                {
                    if (dataElement.Log != null)
                    {
                        LibraryLogger.Debug("DEVPRG LOG: " + dataElement.Log.Value);
                    }
                    else if (dataElement.Response != null)
                    {
                        if (dataElement.Response.Value == "ACK")
                        {
                            if (dataElement.Response.RawMode)
                            {
                                rawModeEnabled = true;
                                LibraryLogger.Debug("Program command ACKed, raw mode enabled.");
                            }
                            else
                            {
                                LibraryLogger.Warning("Program command ACKed, but raw mode NOT indicated. This is unexpected.");
                            }
                            initialResponseReceived = true;
                        }
                        else if (dataElement.Response.Value == "Nak")
                        {
                            LibraryLogger.Error($"Program command NAKed. Message: {dataElement.Response.Value}");
                            return false;
                        }
                        else if (!string.IsNullOrEmpty(dataElement.Response.Value))
                        {
                            LibraryLogger.Warning($"Unexpected response value: {dataElement.Response.Value} while waiting for Program raw mode ACK.");
                        }
                    }
                    else
                    {
                        LibraryLogger.Warning("Received unexpected data payload during Program (rawmode ACK loop).");
                    }
                    if (initialResponseReceived) break;
                }
                if (datas.Length == 0 && !initialResponseReceived)
                {
                    LibraryLogger.Error("Received empty data payload from GetFirehoseResponseDataPayloads (Program rawmode ACK loop), breaking.");
                    return false;
                }
            }

            if (!rawModeEnabled)
            {
                LibraryLogger.Error("Raw mode was not enabled by the device for Program operation. Aborting write.");
                return false;
            }

            // Send the actual data in chunks from the stream
            LibraryLogger.Debug($"Starting to stream {totalBytesToStreamIncludingPadding} bytes of data...");
            Stopwatch transferStopwatch = Stopwatch.StartNew();
            long totalBytesSent = 0;

            int streamChunkSize = (int)Math.Min(1024 * 1024, totalBytesToStreamIncludingPadding); // 1MB or total, whichever is smaller

            byte[] chunkBuffer = new byte[streamChunkSize];

            try
            {
                if (storageType == StorageType.SPINOR) Firehose.Serial.SetTimeOut(300000); // 5 minutes for SPINOR
                else Firehose.Serial.SetTimeOut(30000); // 30 seconds for other types

                while (totalBytesSent < totalBytesToStreamIncludingPadding)
                {
                    int bytesToReadForThisChunk = (int)Math.Min(streamChunkSize, totalBytesToStreamIncludingPadding - totalBytesSent);

                    int bytesActuallyReadFromStream = 0;
                    int currentBufferOffset = 0;
                    while (bytesActuallyReadFromStream < bytesToReadForThisChunk) // Ensure we fill the chunk or hit EOF
                    {
                        int read = inputStream.Read(chunkBuffer, currentBufferOffset, bytesToReadForThisChunk - bytesActuallyReadFromStream);
                        if (read == 0) break; // EOF
                        bytesActuallyReadFromStream += read;
                        currentBufferOffset += read;
                    }

                    byte[] chunkToSend;
                    if (bytesActuallyReadFromStream < bytesToReadForThisChunk)
                    {
                        // Reached EOF of input stream, but still need to send `bytesToReadForThisChunk` (which includes padding)
                        LibraryLogger.Debug($"EOF reached on input stream. Read {bytesActuallyReadFromStream}, expected {bytesToReadForThisChunk}. Padding remaining.");
                        chunkToSend = new byte[bytesToReadForThisChunk];
                        Buffer.BlockCopy(chunkBuffer, 0, chunkToSend, 0, bytesActuallyReadFromStream);
                        // The rest of chunkToSend is already zeros (due to new byte[]).
                    }
                    else
                    {
                        chunkToSend = new byte[bytesActuallyReadFromStream]; // Should be bytesToReadForThisChunk
                        Buffer.BlockCopy(chunkBuffer, 0, chunkToSend, 0, bytesActuallyReadFromStream);
                    }

                    Firehose.Serial.SendLargeRawData(chunkToSend);

                    totalBytesSent += chunkToSend.Length;
                    progressCallback?.Invoke(totalBytesSent, totalBytesToStreamIncludingPadding);
                }
            }
            catch (IOException ioEx)
            {
                LibraryLogger.Error($"IO Error during data streaming for Program: {ioEx.Message}. Sent {totalBytesSent}/{totalBytesToStreamIncludingPadding} bytes.");
                transferStopwatch.Stop();
                return false;
            }
            catch (Exception ex)
            {
                LibraryLogger.Error($"Error sending raw data stream for Program command: {ex.Message}. Sent {totalBytesSent}/{totalBytesToStreamIncludingPadding} bytes.");
                transferStopwatch.Stop();
                return false;
            }

            transferStopwatch.Stop();
            LibraryLogger.Debug($"Raw data stream ({totalBytesSent} bytes) sent in {transferStopwatch.ElapsedMilliseconds} ms.");

            if (totalBytesSent != totalBytesToStreamIncludingPadding)
            {
                LibraryLogger.Error($"Data streaming incomplete. Sent {totalBytesSent}, expected {totalBytesToStreamIncludingPadding}.");
                return false;
            }
            Firehose.Serial.SendZeroLengthPacket();

            // LOOP 2: Wait for final ACK/NAK after data transfer
            bool finalAckReceived = false;
            int finalAckAttempts = 0;

            while (!finalAckReceived && finalAckAttempts < 20)
            {
                finalAckAttempts++;
                Data[] datas = Firehose.GetFirehoseResponseDataPayloads();
                foreach (Data dataElement in datas)
                {
                    if (dataElement.Log != null)
                    {
                        LibraryLogger.Debug("DEVPRG LOG: " + dataElement.Log.Value);
                    }
                    else if (dataElement.Response != null)
                    {
                        if (dataElement.Response.Value == "ACK")
                        {
                            LibraryLogger.Debug("Program operation successful (final ACK received).");
                            finalAckReceived = true;
                        }
                        else if (dataElement.Response.Value == "Nak")
                        {
                            LibraryLogger.Error($"Program operation failed (final NAK received). Message: {dataElement.Response.Value}");
                            return false;
                        }
                        else if (!string.IsNullOrEmpty(dataElement.Response.Value))
                        {
                            LibraryLogger.Warning($"Unexpected response value: {dataElement.Response.Value} while waiting for final Program ACK.");
                        }
                    }
                    else
                    {
                        LibraryLogger.Warning("Received unexpected data payload during Program (final ACK loop).");
                    }
                    if (finalAckReceived) break;
                }
                if (datas.Length == 0 && !finalAckReceived)
                {
                    LibraryLogger.Warning($"Received empty data payload (Program final ACK loop attempt {finalAckAttempts}). Waiting briefly...");
                    System.Threading.Thread.Sleep(500);
                }
            }

            if (!finalAckReceived)
            {
                LibraryLogger.Error("Did not receive a clear final ACK/NAK after Program data transfer.");
                return false;
            }
            return true;
        }

        public static bool Erase(this QualcommFirehose Firehose, StorageType storageType, uint LUNi, uint sectorSize, uint startSector, uint numSectorsToErase)
        {
            LibraryLogger.Debug($"ERASE: LUN{LUNi}, StartSector: {startSector}, NumSectors: {numSectorsToErase}, SectorSize: {sectorSize}");

            if (numSectorsToErase == 0)
            {
                LibraryLogger.Warning("Erase command: numSectorsToErase is 0. Nothing to erase.");
                return true;
            }

            string eraseCommandXml = QualcommFirehoseXml.BuildCommandPacket([
                QualcommFirehoseXmlPackets.GetErasePacket(storageType, LUNi, sectorSize, startSector, numSectorsToErase)
            ]);

            Firehose.Serial.SendData(Encoding.UTF8.GetBytes(eraseCommandXml));

            bool finalAckOrNakReceived = false;
            bool success = false;
            int attempts = 0;

            // Erasing SPINOR can take a while
            Firehose.Serial.SetTimeOut(10000);
            const int maxAttempts = 100;

            while (!finalAckOrNakReceived && attempts < maxAttempts)
            {
                attempts++;
                Data[] datas;
                try
                {
                    datas = Firehose.GetFirehoseResponseDataPayloads();
                }
                catch (TimeoutException)
                {
                    LibraryLogger.Warning($"Timeout waiting for response to Erase command (Attempt {attempts}/{maxAttempts}).");
                    if (attempts < maxAttempts) Thread.Sleep(500);
                    continue;
                }
                catch (BadMessageException bme)
                {
                    LibraryLogger.Warning($"Bad message received for Erase command (Attempt {attempts}/{maxAttempts}): {bme.Message}");
                    if (attempts < maxAttempts) Thread.Sleep(200);
                    continue;
                }

                foreach (Data data in datas)
                {
                    if (data.Log != null) LibraryLogger.Debug("DEVPRG LOG: " + data.Log.Value);
                    else if (data.Response != null)
                    {
                        if (data.Response.Value == "ACK") { LibraryLogger.Debug("Erase command ACKed."); success = true; }
                        else if (data.Response.Value == "Nak") { LibraryLogger.Error($"Erase command NAKed. Details: {data.Response.Value}"); success = false; }
                        else { LibraryLogger.Warning($"Unexpected response value for Erase: {data.Response.Value}"); success = false; }
                        finalAckOrNakReceived = true;
                        break;
                    }
                }
            }
            if (!finalAckOrNakReceived) { LibraryLogger.Error("Failed to get ACK/NAK after sending Erase command and multiple attempts."); return false; }
            return success;
        }

        public static bool Reset(this QualcommFirehose Firehose, PowerValue powerValue = PowerValue.reset, uint delayInSeconds = 1)
        {
            LibraryLogger.Debug("Rebooting phone");

            string Command03 = QualcommFirehoseXml.BuildCommandPacket([
                QualcommFirehoseXmlPackets.GetPowerPacket(powerValue, delayInSeconds)
            ]);

            Firehose.Serial.SendData(Encoding.UTF8.GetBytes(Command03));

            bool GotResponse = false;

            while (!GotResponse)
            {
                Data[] datas = Firehose.GetFirehoseResponseDataPayloads();

                foreach (Data data in datas)
                {
                    if (data.Log != null)
                    {
                        LibraryLogger.Debug("DEVPRG LOG: " + data.Log.Value);
                    }
                    else if (data.Response != null)
                    {
                        GotResponse = true;
                    }
                    else
                    {
                        LibraryLogger.Warning("Why are we here?");
                    }
                }
            }

            // Workaround for problem
            // SerialPort is sometimes not disposed correctly when the device is already removed.
            // So explicitly dispose here
            Firehose.Serial.Close();

            return true;
        }

        public static JSON.StorageInfo.Root? GetStorageInfo(this QualcommFirehose Firehose, StorageType storageType = StorageType.UFS, uint PhysicalPartitionNumber = 0)
        {
            LibraryLogger.Debug("Getting Storage Info");

            string Command03 = QualcommFirehoseXml.BuildCommandPacket([
                QualcommFirehoseXmlPackets.GetStorageInfoPacket(storageType, PhysicalPartitionNumber)
            ]);

            Firehose.Serial.SendData(Encoding.UTF8.GetBytes(Command03));

            bool GotResponse = false;

            string storageInfoJson = null;

            while (!GotResponse)
            {
                Data[] datas = Firehose.GetFirehoseResponseDataPayloads();

                foreach (Data data in datas)
                {
                    if (data.Log != null)
                    {
                        if (data.Log.Value.StartsWith("INFO: {\"storage_info\": "))
                        {
                            storageInfoJson = data.Log.Value.Substring(6);
                        }

                        LibraryLogger.Debug("DEVPRG LOG: " + data.Log.Value);
                    }
                    else if (data.Response != null)
                    {
                        GotResponse = true;
                    }
                    else
                    {
                        LibraryLogger.Warning("Why are we here?");
                    }
                }
            }

            if (storageInfoJson == null)
            {
                LibraryLogger.Warning("Storage info JSON not found in logs, though command was ACKed.");
                return null;
            }

            try
            {
                // Use the source-generated context
                var options = new JsonSerializerOptions
                {
                    TypeInfoResolver = AppJsonSerializerContext.Default // Use the generated context
                };
                // It's also common to just pass the specific JsonTypeInfo:
                // return System.Text.Json.JsonSerializer.Deserialize(storageInfoJson, AppJsonSerializerContext.Default.Root);
                return System.Text.Json.JsonSerializer.Deserialize<JSON.StorageInfo.Root>(storageInfoJson, options);
            }
            catch (NotSupportedException nse)
            {
                LibraryLogger.Error($"Failed to deserialize storage info JSON (AOT/Reflection issue likely): {nse.Message}");
                LibraryLogger.Debug($"JSON content: {storageInfoJson}");
                LibraryLogger.Debug($"Make sure 'QCEDL.NET.Json.AppJsonSerializerContext' includes [JsonSerializable(typeof(Qualcomm.EmergencyDownload.Layers.APSS.Firehose.JSON.StorageInfo.Root))] and for its members if they are custom types.");
                return null;
            }
            catch (System.Text.Json.JsonException jsonEx)
            {
                LibraryLogger.Error($"Failed to deserialize storage info JSON: {jsonEx.Message}");
                LibraryLogger.Debug($"JSON content: {storageInfoJson}");
                return null;
            }
            catch (Exception ex)
            {
                LibraryLogger.Error($"Unexpected error during JSON deserialization: {ex.Message}");
                LibraryLogger.Debug($"JSON content: {storageInfoJson}");
                return null;
            }
        }

        public static bool SendRawXmlAndGetResponse(this QualcommFirehose Firehose, string xmlCommand)
        {
            LibraryLogger.Debug($"Sending Raw XML: {xmlCommand}");
            Firehose.Serial.SendData(Encoding.UTF8.GetBytes(xmlCommand));

            bool finalAckOrNakReceived = false;
            bool success = false;
            int attempts = 0;
            const int maxAttempts = 10;

            while (!finalAckOrNakReceived && attempts < maxAttempts)
            {
                attempts++;
                Data[] datas;
                try
                {
                    datas = Firehose.GetFirehoseResponseDataPayloads(WaitTilFooter: true);
                }
                catch (TimeoutException)
                {
                    LibraryLogger.Warning($"Timeout waiting for response to raw XML (Attempt {attempts}/{maxAttempts}).");
                    if (attempts < maxAttempts) Thread.Sleep(200);
                    continue;
                }
                catch (BadMessageException bme)
                {
                    LibraryLogger.Warning($"Bad message received for raw XML (Attempt {attempts}/{maxAttempts}): {bme.Message}");
                    if (attempts < maxAttempts) Thread.Sleep(200);
                    continue;
                }

                if (datas.Length == 0 && !finalAckOrNakReceived)
                {
                    LibraryLogger.Warning($"No data received in response to raw XML (Attempt {attempts}/{maxAttempts}).");
                    if (attempts < maxAttempts) Thread.Sleep(200);
                    continue;
                }

                foreach (Data data in datas)
                {
                    if (data.Log != null)
                    {
                        LibraryLogger.Debug("DEVPRG LOG: " + data.Log.Value);
                    }
                    else if (data.Response != null)
                    {
                        if (data.Response.Value == "ACK")
                        {
                            LibraryLogger.Debug($"Raw XML command ACKed. RawMode: {data.Response.RawMode}");
                            success = true;
                            finalAckOrNakReceived = true;
                            break;
                        }
                        else if (data.Response.Value == "Nak")
                        {
                            LibraryLogger.Error("Raw XML command NAKed.");
                            success = false;
                            finalAckOrNakReceived = true;
                            break;
                        }
                        else
                        {
                            LibraryLogger.Warning($"Unexpected response value for raw XML: {data.Response.Value}");
                        }
                    }
                    else
                    {
                        LibraryLogger.Warning("Received data payload without Log or Response element for raw XML command.");
                    }
                }
            }

            if (!finalAckOrNakReceived)
            {
                LibraryLogger.Error("Failed to get ACK/NAK after sending raw XML command and multiple attempts.");
                return false;
            }
            return success;
        }
    }
}