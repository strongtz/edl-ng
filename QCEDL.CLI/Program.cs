using System.CommandLine;
using System.Globalization;
using QCEDL.CLI.Commands;
using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

// --- Define Global Options ---
var loaderOption = new Option<FileInfo>(
    aliases: ["--loader", "-l"],
    description: "Path to the Firehose programmer (e.g., prog_firehose_*.elf).")
{
    IsRequired = false
}; // Initially false, commands that need it can enforce it or EdlManager can check
loaderOption.ExistingOnly();

var vidOption = new Option<int?>(
    name: "--vid",
    description: "Specify USB Vendor ID (hex).",
    parseArgument: result =>
    {
        if (result.Tokens.Count == 0)
        {
            return null;
        }

        var vidStr = result.Tokens[0].Value;
        if (int.TryParse(vidStr?.Replace("0x", ""), NumberStyles.HexNumber, null, out var vid))
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
        if (result.Tokens.Count == 0)
        {
            return null;
        }

        var pidStr = result.Tokens[0].Value;
        if (int.TryParse(pidStr?.Replace("0x", ""), NumberStyles.HexNumber, null, out var pid))
        {
            return pid;
        }

        result.ErrorMessage = $"Invalid PID format: {pidStr}. Use hex (e.g., 0x9008 or 0x900E).";
        return null;
    });

var memoryOption = new Option<StorageType?>(
    name: "--memory",
    description:
    $"Set memory type for Firehose operations (e.g., {string.Join(", ", Enum.GetNames<StorageType>())}). Defaults typically to UFS.");

var logLevelOption = new Option<LogLevel>(
    name: "--loglevel",
    description: "Set the logging level.",
    getDefaultValue: () => LogLevel.Info);

var maxPayloadOption = new Option<ulong?>(
    name: "--maxpayload",
    description: "Set max payload size in bytes for Firehose configure command.");

var slotOption = new Option<uint>(
    aliases: ["--slot", "-s"],
    description: "Specify the slot for operations (0 or 1). Defaults to 0.\n" +
                 "This is useful when memory is sdcc. Slot 0 is typically eMMC, and slot 1 is typically sdcard.",
    getDefaultValue: () => 0);
slotOption.AddValidator(result =>
{
    var slotValue = result.GetValueOrDefault<uint>();
    if (slotValue is not 0 and not 1)
    {
        result.ErrorMessage = "Value for --slot must be 0 or 1.";
    }
});

var hostDevAsTargetOption = new Option<string?>(
    name: "--hostdev-as-target",
    description: "Treat the specified host device path as the target for operations (e.g., /dev/mtd0). " +
                 "This bypasses USB Firehose and writes directly to the host device. " +
                 "Currently only supports SPI NOR flash devices with 4K block size.");

var imgSizeOption = new Option<string?>(
    name: "--img-size",
    description: "Size of the image file when using --hostdev-as-target with a file path (e.g., 32M, 1G, 512K). " +
                 "Only used when --hostdev-as-target points to a file that doesn't exist.");

var radxaWosOption = new Option<bool>(
    name: "--radxa-wos-platform",
    description: "Access the Radxa Windows on Snapdragon SPI NOR backend via the local driver (Windows only).");

// --- Create Global Options Binder ---
var globalOptionsBinder = new GlobalOptionsBinder(
    loaderOption,
    vidOption,
    pidOption,
    memoryOption,
    logLevelOption,
    maxPayloadOption,
    slotOption,
    hostDevAsTargetOption,
    imgSizeOption,
    radxaWosOption
);

// --- Define Root Command ---
var rootCommand = new RootCommand("edl-ng - Qualcomm Emergency Download CLI");

rootCommand.AddGlobalOption(loaderOption);
rootCommand.AddGlobalOption(vidOption);
rootCommand.AddGlobalOption(pidOption);
rootCommand.AddGlobalOption(memoryOption);
rootCommand.AddGlobalOption(logLevelOption);
rootCommand.AddGlobalOption(maxPayloadOption);
rootCommand.AddGlobalOption(slotOption);
rootCommand.AddGlobalOption(hostDevAsTargetOption);
rootCommand.AddGlobalOption(imgSizeOption);
rootCommand.AddGlobalOption(radxaWosOption);

// --- Define Commands (Add more commands here later) ---
rootCommand.AddCommand(UploadLoaderCommand.Create(globalOptionsBinder));
rootCommand.AddCommand(ResetCommand.Create(globalOptionsBinder));
rootCommand.AddCommand(PrintGptCommand.Create(globalOptionsBinder));
rootCommand.AddCommand(ReadPartitionCommand.Create(globalOptionsBinder));
rootCommand.AddCommand(ReadSectorCommand.Create(globalOptionsBinder));
rootCommand.AddCommand(ReadSectorCommand.CreateReadLunCommand(globalOptionsBinder));
rootCommand.AddCommand(DumpRawprogramCommand.Create(globalOptionsBinder));
rootCommand.AddCommand(WritePartitionCommand.Create(globalOptionsBinder));
rootCommand.AddCommand(WriteSectorCommand.Create(globalOptionsBinder));
rootCommand.AddCommand(ErasePartitionCommand.Create(globalOptionsBinder));
rootCommand.AddCommand(EraseSectorCommand.Create(globalOptionsBinder));
rootCommand.AddCommand(ProvisionCommand.Create(globalOptionsBinder));
rootCommand.AddCommand(RawProgramCommand.Create(globalOptionsBinder));
// ... etc ...

// --- Default Handler (Show Help if no command given) ---
rootCommand.SetHandler(async _ => await rootCommand.InvokeAsync(["--help"]));

// --- Invoke Parser ---
return await rootCommand.InvokeAsync(args);