using System.CommandLine;
using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;
using Qualcomm.EmergencyDownload.Core;
using Qualcomm.EmergencyDownload.Helpers;

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

    private static async Task<int> ExecuteAsync(EdlOptions globalOptions, FileInfo xmlFile)
    {
        Logging.Log($"Executing 'provision' command with XML file: {xmlFile.FullName}", LogLevel.Trace);
        return await CommandExecutor.RunAsync("provision", async () =>
        {
            using var manager = new EdlManager(globalOptions);
            return await ProvisionRunner.RunAsync(manager, xmlFile);
        });
    }
}