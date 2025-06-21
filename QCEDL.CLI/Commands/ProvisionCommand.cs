using System.CommandLine;
using System.Xml.Linq;
using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace QCEDL.CLI.Commands;

internal sealed class ProvisionCommand
{
    private static readonly Argument<FileInfo> XmlFileArgument =
        new("xmlfile", "Path to the UFS provisioning XML file.")
        { Arity = ArgumentArity.ExactlyOne };

    public static Command Create(GlobalOptionsBinder globalOptionsBinder)
    {
        var command = new Command("provision", "Performs UFS provisioning using an XML file.")
        {
            XmlFileArgument
        };
        _ = XmlFileArgument.ExistingOnly();
        command.SetHandler(ExecuteAsync, globalOptionsBinder, XmlFileArgument);
        return command;
    }

    private static async Task<int> ExecuteAsync(GlobalOptionsBinder globalOptions, FileInfo xmlFile)
    {
        Logging.Log($"Executing 'provision' command with XML file: {xmlFile.FullName}", LogLevel.Trace);

        var effectiveStorageType = StorageType.Ufs;
        if (globalOptions.MemoryType.HasValue && globalOptions.MemoryType != StorageType.Ufs)
        {
            Logging.Log($"Warning: --memory is set to '{globalOptions.MemoryType}'. UFS provisioning command implies UFS. Using UFS for this operation.", LogLevel.Warning);
        }

        try
        {
            using var manager = new EdlManager(globalOptions);
            await manager.EnsureFirehoseModeAsync();

            Logging.Log("Sending initial Firehose configure command (Memory: UFS, SkipStorageInit: true)...");
            // Explicitly use UFS and skipStorageInit=true for this specific configure call.
            var configureSuccess = await Task.Run(() => manager.Firehose.Configure(effectiveStorageType, skipStorageInit: true));
            if (!configureSuccess)
            {
                Logging.Log("Failed to send initial Firehose configure command for provisioning.", LogLevel.Error);
                return 1;
            }
            Logging.Log("Initial Firehose configure command sent successfully.");

            manager.FlushForResponse();

            XDocument doc;
            try
            {
                doc = XDocument.Load(xmlFile.FullName);
            }
            catch (Exception ex)
            {
                Logging.Log($"Error parsing XML file '{xmlFile.FullName}': {ex.Message}", LogLevel.Error);
                return 1;
            }

            if (doc.Root == null || doc.Root.Name != "data")
            {
                Logging.Log("Invalid XML structure: Root element must be <data>.", LogLevel.Error);
                return 1;
            }

            var ufsElements = doc.Root.Elements("ufs").ToList();
            if (ufsElements.Count == 0)
            {
                Logging.Log("No <ufs> elements found in the XML file.", LogLevel.Warning);
                return 0;
            }

            Logging.Log($"Found {ufsElements.Count} <ufs> elements to process.", LogLevel.Debug);

            var successCount = 0;
            var commandIndex = 0;
            foreach (var ufsElement in ufsElements)
            {
                commandIndex++;
                var ufsElementString = ufsElement.ToString(SaveOptions.DisableFormatting);
                var fullXmlPayload = $"<?xml version=\"1.0\" ?><data>{ufsElementString}</data>";

                Logging.Log($"Sending UFS command {commandIndex}/{ufsElements.Count}");

                var success = await Task.Run(() => manager.Firehose.SendRawXmlAndGetResponse(fullXmlPayload));

                if (success)
                {
                    Logging.Log($"UFS command {commandIndex} ACKed.");
                    successCount++;
                }
                else
                {
                    Logging.Log($"Failed to send UFS command {commandIndex} or received NAK: {ufsElementString}", LogLevel.Error);
                    Logging.Log($"Aborting provisioning. {successCount}/{ufsElements.Count} commands succeeded before failure.", LogLevel.Error);
                    return 1;
                }
            }

            Logging.Log($"UFS provisioning completed. All {successCount}/{ufsElements.Count} commands sent and ACKed successfully.");
        }
        catch (FileNotFoundException ex)
        {
            Logging.Log(ex.Message, LogLevel.Error);
            return 1;
        }
        catch (ArgumentException ex)
        {
            Logging.Log($"Argument Error: {ex.Message}", LogLevel.Error);
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            Logging.Log($"Operation Error: {ex.Message}", LogLevel.Error);
            return 1;
        }
        catch (IOException ex)
        {
            Logging.Log($"IO Error: {ex.Message}", LogLevel.Error);
            return 1;
        }
        catch (Exception ex)
        {
            Logging.Log($"An unexpected error occurred in 'provision': {ex.Message}", LogLevel.Error);
            Logging.Log(ex.ToString(), LogLevel.Debug);
            return 1;
        }
        return 0;
    }
}