using QCEDL.NET.Logging;

namespace Qualcomm.EmergencyDownload.Transport;

public static class QualcommTransportExtensions
{
    public static void SendData(this IQualcommTransport transport, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(data);

        LibraryLogger.Trace($"Sending {data.Length} bytes via {transport.Backend}.");
        var bytesWritten = transport.Write(data, 0, data.Length);
        if (bytesWritten != data.Length)
        {
            throw new IOException(
                $"{transport.Backend} short write: wrote {bytesWritten} of {data.Length} bytes.");
        }
    }

    public static void SendLargeRawData(this IQualcommTransport transport, byte[] data)
    {
        transport.SendData(data);
    }

    public static byte[] SendCommand(this IQualcommTransport transport, byte[] command,
        byte[]? responsePattern)
    {
        transport.SendData(command);
        return transport.GetResponse(responsePattern);
    }

    public static byte[] GetResponse(this IQualcommTransport transport, byte[]? responsePattern,
        int length = 0x2000)
    {
        ArgumentNullException.ThrowIfNull(transport);
        if (length <= 0)
        {
            length = 0x2000;
        }

        var responseBuffer = new byte[length];
        var bytesRead = transport.Read(responseBuffer, 0, responseBuffer.Length);
        if (bytesRead == 0)
        {
            LibraryLogger.Warning("Emergency mode of phone is ignoring us");
            throw new BadMessageException("The device returned an empty response.");
        }

        var response = responseBuffer.AsSpan(0, bytesRead).ToArray();
        if (responsePattern is null)
        {
            return response;
        }

        if (response.Length < responsePattern.Length ||
            !response.AsSpan(0, responsePattern.Length).SequenceEqual(responsePattern))
        {
            var logLength = Math.Min(response.Length, 0x10);
            LibraryLogger.Error("Qualcomm response: " +
                                Converter.ConvertHexToString(response.AsSpan(0, logLength).ToArray(), ""));
            LibraryLogger.Error("Expected: " + Converter.ConvertHexToString(responsePattern, ""));
            throw new BadMessageException("The device response did not match the expected pattern.");
        }

        return response;
    }
}

public class BadMessageException : Exception
{
    public BadMessageException()
    {
    }

    public BadMessageException(string message) : base(message)
    {
    }

    public BadMessageException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public class BadConnectionException : Exception
{
    public BadConnectionException()
    {
    }

    public BadConnectionException(string message) : base(message)
    {
    }

    public BadConnectionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}