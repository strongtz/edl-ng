using System.Xml.Linq;
using Qualcomm.EmergencyDownload.Helpers;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace Qualcomm.EmergencyDownload.Core;

/// <summary>Shared orchestration for the UFS <c>provision</c> flow.</summary>
public static class ProvisionRunner
{
    public static async Task<int> RunAsync(EdlManager manager, FileInfo xmlFile, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(xmlFile);

        if (!xmlFile.Exists)
        {
            Logging.Log($"Provisioning XML file not found: {xmlFile.FullName}", LogLevel.Error);
            return 1;
        }

        if (manager.Options.MemoryType.HasValue && manager.Options.MemoryType != StorageType.Ufs)
        {
            Logging.Log($"--memory is '{manager.Options.MemoryType}', but provisioning implies UFS. Using UFS.", LogLevel.Warning);
        }

        await manager.EnsureFirehoseModeAsync();

        Logging.Log("Sending initial Firehose configure command (Memory: UFS, SkipStorageInit: true)...");
        var configured = await Task.Run(() => manager.Firehose.Configure(StorageType.Ufs, skipStorageInit: true), ct);
        if (!configured)
        {
            Logging.Log("Failed to send initial Firehose configure command for provisioning.", LogLevel.Error);
            return 1;
        }
        manager.FlushForResponse();

        XDocument doc;
        try
        {
            doc = XDocument.Load(xmlFile.FullName);
        }
        catch (Exception ex)
        {
            Logging.Log($"Error parsing XML '{xmlFile.FullName}': {ex.Message}", LogLevel.Error);
            return 1;
        }

        if (doc.Root is null || doc.Root.Name != "data")
        {
            Logging.Log("Invalid XML: root must be <data>.", LogLevel.Error);
            return 1;
        }

        var ufsElements = doc.Root.Elements("ufs").ToList();
        if (ufsElements.Count == 0)
        {
            Logging.Log("No <ufs> elements found.", LogLevel.Warning);
            return 0;
        }

        Logging.Log($"Found {ufsElements.Count} <ufs> elements to process.", LogLevel.Debug);

        var success = 0;
        var index = 0;
        foreach (var ufsEl in ufsElements)
        {
            ct.ThrowIfCancellationRequested();
            index++;
            var payload = $"<?xml version=\"1.0\" ?><data>{ufsEl.ToString(SaveOptions.DisableFormatting)}</data>";
            Logging.Log($"Sending UFS command {index}/{ufsElements.Count}");
            var ok = await Task.Run(() => manager.Firehose.SendRawXmlAndGetResponse(payload), ct);
            if (!ok)
            {
                Logging.Log($"UFS command {index} NAK/failed. {success}/{ufsElements.Count} succeeded before failure.", LogLevel.Error);
                return 1;
            }
            success++;
        }

        Logging.Log($"UFS provisioning completed: {success}/{ufsElements.Count} commands ACKed.");
        return 0;
    }
}