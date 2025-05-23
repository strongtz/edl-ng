using System.CommandLine;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using QCEDL.CLI.Commands;
using QCEDL.CLI.Core;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

var serviceCollection = new ServiceCollection();

// --- Define Global Options ---
var loaderOption = new Option<FileInfo>(
    aliases: ["--loader", "-l"],
    description: "Path to the Firehose programmer (e.g., prog_firehose_*.elf).")
{
    IsRequired = false
}.ExistingOnly(); // Initially false, commands that need it can enforce it or EdlManager can check

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
        if (int.TryParse(vidStr.Replace("0x", ""), NumberStyles.HexNumber, null, out var vid))
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

        result.ErrorMessage = $"Invalid PID format: {pidStr}. Use hex (e.g., 0x9008).";
        return null;
    });

var memoryOption = new Option<StorageType?>(
    name: "--memory",
    description:
    $"Set memory type for Firehose operations (e.g., {string.Join(", ", Enum.GetNames<StorageType>())}). Defaults typically to UFS.");

var logLevelOption = new Option<LogLevel>(
    name: "--loglevel",
    description: "Set the logging level.",
    getDefaultValue: () => LogLevel.Information);

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
    if (slotValue != 0 && slotValue != 1)
    {
        result.ErrorMessage = "Value for --slot must be 0 or 1.";
    }
});

// Logging
var logLevel = logLevelOption.Parse(args).GetValueForOption(logLevelOption);
serviceCollection
    .AddLogging(builder =>
    {
        builder
            .AddFilter(level => level >= logLevel)
            .AddSimpleConsole(options =>
            {
                options.IncludeScopes = true;
                options.SingleLine = true;
                options.ColorBehavior = LoggerColorBehavior.Enabled;
                options.TimestampFormat = "HH:mm:ss ";
            });
    });

// Commands
serviceCollection
    .AddScoped<ICommand, ErasePartitionCommand>()
    .AddScoped<ICommand, EraseSectorCommand>()
    .AddScoped<ICommand, PrintGptCommand>()
    .AddScoped<ICommand, ProvisionCommand>()
    .AddScoped<ICommand, RawProgramCommand>()
    .AddScoped<ICommand, ReadPartitionCommand>()
    .AddScoped<ICommand, ReadSectorCommand>()
    .AddScoped<ICommand, ResetCommand>()
    .AddScoped<ICommand, UploadLoaderCommand>()
    .AddScoped<ICommand, WritePartitionCommand>()
    .AddScoped<ICommand, WriteSectorCommand>();

serviceCollection
    .AddTransient<IEdlManager, EdlManager>()
    .AddSingleton<IEdlManagerProvider, EdlManagerProvider>();

// --- Create Global Options Binder ---
var globalOptionsBinder = new GlobalOptionsBinder(
    loaderOption,
    vidOption,
    pidOption,
    memoryOption,
    logLevelOption,
    maxPayloadOption,
    slotOption
);
serviceCollection
    .AddSingleton(globalOptionsBinder);

await using var serviceProvider = serviceCollection.BuildServiceProvider();

// --- Define Root Command ---
var rootCommand = new RootCommand("edl-ng - Qualcomm Emergency Download CLI");

rootCommand.AddGlobalOption(loaderOption);
rootCommand.AddGlobalOption(vidOption);
rootCommand.AddGlobalOption(pidOption);
rootCommand.AddGlobalOption(memoryOption);
rootCommand.AddGlobalOption(logLevelOption);
rootCommand.AddGlobalOption(maxPayloadOption);
rootCommand.AddGlobalOption(slotOption);

foreach (var command in serviceProvider.GetServices<ICommand>())
{
    rootCommand.AddCommand(command.Create());
}

// --- Default Handler (Show Help if no command given) ---
rootCommand.SetHandler(async _ =>
{
    await rootCommand.InvokeAsync(["--help"]);
});

// --- Invoke Parser ---
return await rootCommand.InvokeAsync(args);