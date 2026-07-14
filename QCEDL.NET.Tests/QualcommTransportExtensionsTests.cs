using Qualcomm.EmergencyDownload.Transport;

namespace QCEDL.NET.Tests;

public sealed class QualcommTransportExtensionsTests
{
    [Fact]
    public void SendDataRejectsShortWrite()
    {
        using var transport = new StubTransport { BytesWritten = 2 };

        _ = Assert.Throws<IOException>(() => transport.SendData([1, 2, 3]));
    }

    [Fact]
    public void GetResponseRejectsPatternLongerThanResponse()
    {
        using var transport = new StubTransport { Response = [1, 2] };

        _ = Assert.Throws<BadMessageException>(() => transport.GetResponse([1, 2, 3]));
    }

    [Fact]
    public void GetResponseReturnsOnlyTransferredBytes()
    {
        using var transport = new StubTransport { Response = [1, 2, 3] };

        var response = transport.GetResponse([1, 2], 32);

        Assert.Equal(new byte[] { 1, 2, 3 }, response);
    }
}

internal sealed class StubTransport : IQualcommTransport
{
    internal int BytesWritten { get; init; } = -1;
    internal byte[] Response { get; init; } = [];

    public TransportBackend Backend => TransportBackend.WindowsQud;
    public int TimeoutMilliseconds { get; set; } = 1000;

    public int Read(byte[] buffer, int offset, int count)
    {
        Buffer.BlockCopy(Response, 0, buffer, offset, Response.Length);
        return Response.Length;
    }

    public int Write(byte[] buffer, int offset, int count)
    {
        return BytesWritten < 0 ? count : BytesWritten;
    }

    public void SendZeroLengthPacket()
    {
    }

    public void Dispose()
    {
    }
}