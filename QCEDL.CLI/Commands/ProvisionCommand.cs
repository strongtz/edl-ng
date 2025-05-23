using System.CommandLine;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using QCEDL.CLI.Core;
using QCEDL.CLI.Logging;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace QCEDL.CLI.Commands;

internal sealed class ProvisionCommand(
    ILogger<ProvisionCommand> logger,
    GlobalOptionsBinder globalOptionsBinder,
    IEdlManagerProvider edlManagerProvider) : ICommand
{
    private static readonly Argument<FileInfo> XmlFileArgument =
        new("xmlfile", "Path to the UFS provisioning XML file.") { Arity = ArgumentArity.ExactlyOne };

    static ProvisionCommand()
    {
        XmlFileArgument.ExistingOnly();
    }

    public Command Create()
    {
        var command = new Command("provision", "Performs UFS provisioning using an XML file.") { XmlFileArgument };

        command.SetHandler(
            ExecuteAsync,
            globalOptionsBinder,
            XmlFileArgument);

        return command;
    }

    private async Task<int> ExecuteAsync(GlobalOptionsBinder globalOptions, FileInfo xmlFile)
    {
        logger.ExecutingProvisionCommand(xmlFile.FullName);

        var effectiveStorageType = StorageType.UFS;
        if (globalOptions.MemoryType is not null && globalOptions.MemoryType != StorageType.UFS)
        {
            logger.MemoryOptionWarning(globalOptions.MemoryType);
        }

        try
        {
            using var manager = edlManagerProvider.CreateEdlManager();
            await manager.EnsureFirehoseModeAsync();

            logger.SendingInitialConfigure();
            // Explicitly use UFS and skipStorageInit=true for this specific configure call.
            var configureSuccess =
                await Task.Run(() => manager.Firehose.Configure(effectiveStorageType, skipStorageInit: true));
            if (!configureSuccess)
            {
                logger.InitialConfigureSuccess();
                return 1;
            }

            logger.FailedInitialConfigure();

            manager.FlushForResponse();

            XDocument doc;
            try
            {
                doc = XDocument.Load(xmlFile.FullName);
            }
            catch (Exception ex)
            {
                logger.ErrorParsingXml(xmlFile.FullName, ex);
                return 1;
            }

            if (doc.Root == null || doc.Root.Name != "data")
            {
                logger.InvalidXmlStructure();
                return 1;
            }

            var ufsElements = doc.Root.Elements("ufs").ToList();
            if (ufsElements.Count == 0)
            {
                logger.NoUfsElements();
                return 0;
            }

            logger.FoundUfsElements(ufsElements.Count);

            var successCount = 0;
            var commandIndex = 0;
            foreach (var ufsElement in ufsElements)
            {
                commandIndex++;
                var ufsElementString = ufsElement.ToString(SaveOptions.DisableFormatting);
                var fullXmlPayload = $"<?xml version=\"1.0\" ?><data>{ufsElementString}</data>";

                logger.SendingUfsCommand(commandIndex, ufsElements.Count);

                var success = await Task.Run(() => manager.Firehose.SendRawXmlAndGetResponse(fullXmlPayload));

                if (success)
                {
                    logger.UfsCommandAcked(commandIndex);
                    successCount++;
                }
                else
                {
                    logger.FailedUfsCommand(commandIndex, ufsElementString);
                    logger.AbortingProvisioning(successCount, ufsElements.Count);
                    return 1;
                }
            }

            logger.UfsProvisioningCompleted(successCount, ufsElements.Count);
        }
        catch (FileNotFoundException ex)
        {
            logger.ExceptedException(ex);
            return 1;
        }
        catch (ArgumentException ex)
        {
            logger.ExceptedException(ex);
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            logger.ExceptedException(ex);
            return 1;
        }
        catch (IOException ex)
        {
            logger.ExceptedException(ex);
            return 1;
        }
        catch (Exception ex)
        {
            logger.UnexceptedException(ex);
            return 1;
        }

        return 0;
    }
}