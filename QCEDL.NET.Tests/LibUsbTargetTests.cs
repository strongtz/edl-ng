using Qualcomm.EmergencyDownload.Transport;

namespace QCEDL.NET.Tests;

public sealed class LibUsbTargetTests
{
    [Theory]
    [InlineData("usb:vid_05C6,pid_9008,bus_1,addr_2", (byte)1, (byte)2)]
    [InlineData("USB:VID_05C6,PID_900E,BUS_255,ADDR_255", (byte)255, (byte)255)]
    public void ParseDeviceLocationPreservesPinnedDevice(string path, byte bus, byte address)
    {
        Assert.Equal((bus, address), LibUsbTransport.ParseDeviceLocation(path));
    }

    [Theory]
    [InlineData("usb:vid_05C6,pid_9008,bus_1")]
    [InlineData("usb:vid_05C6,pid_9008,addr_2")]
    [InlineData("usb:vid_05C6,pid_9008,bus_256,addr_2")]
    [InlineData("usb:vid_05C6,pid_9008,bus_bad,addr_2")]
    [InlineData("usb:vid_05C6,pid_9008,bus_,addr_")]
    public void IncompleteOrInvalidPinDoesNotFallBackToAnotherDevice(string path)
    {
        _ = Assert.Throws<ArgumentException>(() => LibUsbTransport.ParseDeviceLocation(path));
    }

    [Fact]
    public void UnpinnedDeviceKeepsVidPidDiscovery()
    {
        Assert.Equal((null, null), LibUsbTransport.ParseDeviceLocation("usb:vid_05C6,pid_9008"));
    }
}
