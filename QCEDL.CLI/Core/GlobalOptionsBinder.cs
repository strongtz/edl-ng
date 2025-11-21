using System.CommandLine;
using System.CommandLine.Binding;
using QCEDL.CLI.Helpers;
using QCEDL.NET.Logging;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;
using LogLevel = QCEDL.CLI.Helpers.LogLevel;

namespace QCEDL.CLI.Core;

/// <summary>
/// Binds global command line options to properties.
/// </summary>
internal sealed class GlobalOptionsBinder(
    Option<FileInfo> loaderOption,
    Option<int?> vidOption,
    Option<int?> pidOption,
    Option<StorageType?> memoryOption,
    Option<LogLevel> logLevelOption,
    Option<ulong?> maxPayloadOption,
    Option<uint> slotOption,
    Option<string?> hostDevAsTargetOption,
    Option<string?> imgSizeOption,
    Option<bool> radxaWosOption)
    : BinderBase<GlobalOptionsBinder>
{
    public string? LoaderPath { get; set; }
    public int? Vid { get; set; }
    public int? Pid { get; set; }
    public StorageType? MemoryType { get; set; }
    public LogLevel LogLevel { get; set; }
    public ulong? MaxPayloadSize { get; set; }
    public uint Slot { get; set; }
    public string? HostDevAsTarget { get; set; }
    public string? ImgSize { get; set; }
    public bool RadxaWosPlatform { get; set; }

    protected override GlobalOptionsBinder GetBoundValue(BindingContext bindingContext)
    {
        var cliLogLevel = bindingContext.ParseResult.GetValueForOption(logLevelOption);
        Logging.CurrentLogLevel = cliLogLevel;

        LibraryLogger.LogAction = (message, netLogLevel, memberName, sourceFilePath, sourceLineNumber) =>
        {
            var mappedCliLevel = (LogLevel)netLogLevel;
            Logging.Log(message, mappedCliLevel);
        };

        return new(loaderOption, vidOption, pidOption, memoryOption, logLevelOption, maxPayloadOption, slotOption, hostDevAsTargetOption, imgSizeOption, radxaWosOption)
        {
            LoaderPath = bindingContext.ParseResult.GetValueForOption(loaderOption)?.FullName,
            Vid = bindingContext.ParseResult.GetValueForOption(vidOption),
            Pid = bindingContext.ParseResult.GetValueForOption(pidOption),
            MemoryType = bindingContext.ParseResult.GetValueForOption(memoryOption),
            LogLevel = cliLogLevel,
            MaxPayloadSize = bindingContext.ParseResult.GetValueForOption(maxPayloadOption),
            Slot = bindingContext.ParseResult.GetValueForOption(slotOption),
            HostDevAsTarget = bindingContext.ParseResult.GetValueForOption(hostDevAsTargetOption),
            ImgSize = bindingContext.ParseResult.GetValueForOption(imgSizeOption),
            RadxaWosPlatform = bindingContext.ParseResult.GetValueForOption(radxaWosOption)
        };
    }
}