using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;

namespace QCEDL.CLI.Core;

internal interface IEdlManager : IDisposable
{
    DeviceMode CurrentMode { get; }
    QualcommFirehose Firehose { get; }
    bool IsFirehoseMode { get; }

    /// <summary>
    /// Attempts to detect the current operating mode of the connected EDL device.
    /// Connects temporarily if not already connected.
    /// </summary>
    /// <returns>The detected DeviceMode.</returns>
    Task<DeviceMode> DetectCurrentModeAsync(bool forceReconnect = false);

    /// <summary>
    /// Ensures the device is connected and in Firehose mode, uploading the loader if necessary.
    /// </summary>
    Task EnsureFirehoseModeAsync();

    void FlushForResponse();

    /// <summary>
    /// Connects via Sahara and uploads the specified Firehose programmer.
    /// </summary>
    Task UploadLoaderViaSaharaAsync();

    /// <summary>
    /// Sends the initial Firehose configuration command.
    /// </summary>
    Task ConfigureFirehoseAsync();
}