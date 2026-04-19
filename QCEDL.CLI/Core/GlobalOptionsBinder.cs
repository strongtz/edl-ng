using System.CommandLine;
using System.CommandLine.Binding;
using QCEDL.NET.Logging;
using Qualcomm.EmergencyDownload.Core;
using Qualcomm.EmergencyDownload.Helpers;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;
using LogLevel = Qualcomm.EmergencyDownload.Helpers.LogLevel;

namespace QCEDL.CLI.Core;

/// <summary>
/// Binds global command line options to an <see cref="EdlOptions"/> POCO that is
/// consumable from both the CLI and the GUI.
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
    : BinderBase<EdlOptions>
{
    protected override EdlOptions GetBoundValue(BindingContext bindingContext)
    {
        var cliLogLevel = bindingContext.ParseResult.GetValueForOption(logLevelOption);
        Logging.CurrentLogLevel = cliLogLevel;

        LibraryLogger.LogAction = (message, netLogLevel, memberName, sourceFilePath, sourceLineNumber) =>
        {
            var mappedCliLevel = (LogLevel)netLogLevel;
            Logging.Log(message, mappedCliLevel);
        };

        return new EdlOptions
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
            RadxaWosPlatform = bindingContext.ParseResult.GetValueForOption(radxaWosOption),
        };
    }
}