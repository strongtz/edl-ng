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
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;
using Qualcomm.EmergencyDownload.Transport;
using System.Text;

namespace Qualcomm.EmergencyDownload.Layers.APSS.Firehose;

public class QualcommFirehose(QualcommSerial serial)
{
    public QualcommSerial Serial { get; } = serial;

    public byte[] GetFirehoseXMLResponseBuffer(bool WaitTilFooter = false)
    {
        if (!WaitTilFooter)
        {
            return Serial.GetResponse(null);
        }

        // Optimized for WaitTilFooter = true
        var bufferList = new List<byte>(1024); // Pre-allocate a reasonable starting size
        const int readChunkSize = 256; // Read in chunks of this size
        var footerPattern = Encoding.UTF8.GetBytes(" /></data>");
        // int patternMatchIndex = 0; // unused
        var safetyReadLimit = 8192; // Max 8KB for an XML ACK, to prevent infinite loop
        while (bufferList.Count < safetyReadLimit)
        {
            var chunk = Serial.GetResponse(null, Length: readChunkSize);
            if (chunk == null || chunk.Length == 0)
            {
                // Timeout or no data from device
                LibraryLogger.Warning("Timeout or no data received while waiting for XML footer.");
                break;
            }
            bufferList.AddRange(chunk);
            // Efficiently check for footerPattern in the newly added chunk or at the end of bufferList
            // This is a simplified check; more robust would be a sliding window search
            // For performance, avoid converting bufferList to string repeatedly in the loop.
            // Check if the end of bufferList now contains the footer.
            if (bufferList.Count >= footerPattern.Length)
            {
                var found = true;
                for (var i = 0; i < footerPattern.Length; i++)
                {
                    if (bufferList[bufferList.Count - footerPattern.Length + i] != footerPattern[i])
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                {
                    return bufferList.ToArray();
                }
            }
        }

        LibraryLogger.Error($"XML response footer not found or exceeded safety limit ({safetyReadLimit} bytes). Buffer size: {bufferList.Count}");
        // Return what we have, or throw, depending on how GetFirehoseResponseDataPayloads handles partial XML
        return bufferList.ToArray();
    }

    public Data[] GetFirehoseResponseDataPayloads(bool WaitTilFooter = false)
    {
        var ResponseBuffer = GetFirehoseXMLResponseBuffer(WaitTilFooter);

        if (ResponseBuffer == null || ResponseBuffer.Length == 0 || ResponseBuffer.All(t => t == 0x0))
        {
            return [];
        }

        var Incoming = Encoding.UTF8.GetString(ResponseBuffer);

        try
        {
            var datas = QualcommFirehoseXml.GetDataPayloads(Incoming);
            return datas;
        }
        catch (Exception ex) // Catch specific XML parsing exceptions if possible
        {
            LibraryLogger.Error("UNEXPECTED PARSING FAILURE. ABOUT TO CRASH. PAYLOAD BYTE RAW AS FOLLOW:");
            LibraryLogger.Error(Convert.ToHexString(ResponseBuffer));
            LibraryLogger.Error($"Exception: {ex.Message}");
            throw;
        }
    }
}