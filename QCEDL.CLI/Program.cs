using QCEDL.CLI.Commands;
using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace QCEDL.CLI
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            // --- Define Global Options ---
            var loaderOption = new Option<FileInfo?>(
                aliases: ["--loader", "-l"],
                description: "Path to the Firehose programmer (e.g., prog_firehose_*.elf).")
            { IsRequired = false }; // Initially false, commands that need it can enforce it or EdlManager can check
            loaderOption.ExistingOnly();

            var vidOption = new Option<int?>(
                name: "--vid",
                description: "Specify USB Vendor ID (hex).",
                parseArgument: result =>
                {
                    if (result.Tokens.Count == 0) return null;
                    string? vidStr = result.Tokens[0].Value;
                    if (int.TryParse(vidStr?.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out int vid))
                    {
                        return vid;
                    }
                    result.ErrorMessage = $"Invalid VID format: {vidStr}. Use hex (e.g., 0x05C6).";
                    return null;
                });

            var pidOption = new Option<int?>(
                 name: "--pid",
                 description: "Specify USB Product ID (hex).",
                 parseArgument: result =>
                 {
                     if (result.Tokens.Count == 0) return null;
                     string? pidStr = result.Tokens[0].Value;
                     if (int.TryParse(pidStr?.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out int pid))
                     {
                         return pid;
                     }
                     result.ErrorMessage = $"Invalid PID format: {pidStr}. Use hex (e.g., 0x9008).";
                     return null;
                 });

            var memoryOption = new Option<StorageType?>(
               name: "--memory",
               description: $"Set memory type for Firehose operations (e.g., {string.Join(", ", Enum.GetNames<StorageType>())}). Defaults typically to UFS.");

            var logLevelOption = new Option<LogLevel>(
               name: "--loglevel",
               description: "Set the logging level.",
               getDefaultValue: () => LogLevel.Info);

            var maxPayloadOption = new Option<ulong?>(
               name: "--maxpayload",
               description: "Set max payload size in bytes for Firehose configure command.");

            // --- Create Global Options Binder ---
            var globalOptionsBinder = new GlobalOptionsBinder(
               loaderOption,
               vidOption,
               pidOption,
               memoryOption,
               logLevelOption,
               maxPayloadOption
           );

            // --- Define Root Command ---
            var rootCommand = new RootCommand("edl-ng - Qualcomm Emergency Download CLI");

            rootCommand.AddGlobalOption(loaderOption);
            rootCommand.AddGlobalOption(vidOption);
            rootCommand.AddGlobalOption(pidOption);
            rootCommand.AddGlobalOption(memoryOption);
            rootCommand.AddGlobalOption(logLevelOption);
            rootCommand.AddGlobalOption(maxPayloadOption);

            // --- Define Commands (Add more commands here later) ---
            rootCommand.AddCommand(UploadLoaderCommand.Create(globalOptionsBinder));
            rootCommand.AddCommand(ResetCommand.Create(globalOptionsBinder));
            rootCommand.AddCommand(PrintGptCommand.Create(globalOptionsBinder));
            rootCommand.AddCommand(ReadSectorCommand.Create(globalOptionsBinder));
            rootCommand.AddCommand(ReadPartitionCommand.Create(globalOptionsBinder));
            rootCommand.AddCommand(WriteSectorCommand.Create(globalOptionsBinder));
            rootCommand.AddCommand(WritePartitionCommand.Create(globalOptionsBinder));
            rootCommand.AddCommand(ProvisionCommand.Create(globalOptionsBinder));
            rootCommand.AddCommand(RawProgramCommand.Create(globalOptionsBinder));
            rootCommand.AddCommand(EraseSectorCommand.Create(globalOptionsBinder));
            rootCommand.AddCommand(ErasePartitionCommand.Create(globalOptionsBinder));
            // ... etc ...

            // --- Default Handler (Show Help if no command given) ---
            rootCommand.SetHandler(async (InvocationContext context) =>
            {
                var helpArgs = new string[] { "--help" };
                await rootCommand.InvokeAsync(helpArgs);
                return;
            });


            // --- Invoke Parser ---
            return await rootCommand.InvokeAsync(args);
        }
    }
}
