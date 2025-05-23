using System.CommandLine;
using System.CommandLine.Binding;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

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
    Option<uint> slotOption)
    : BinderBase<GlobalOptionsBinder>
{
    public string? LoaderPath { get; private set; }
    public int? Vid { get; private set; }
    public int? Pid { get; private set; }
    public StorageType? MemoryType { get; private set; }
    public ulong? MaxPayloadSize { get; private set; }
    public uint Slot { get; private set; }

    protected override GlobalOptionsBinder GetBoundValue(BindingContext bindingContext)
    {
        return new(loaderOption, vidOption, pidOption, memoryOption, logLevelOption, maxPayloadOption, slotOption)
        {
            LoaderPath = bindingContext.ParseResult.GetValueForOption(loaderOption)?.FullName,
            Vid = bindingContext.ParseResult.GetValueForOption(vidOption),
            Pid = bindingContext.ParseResult.GetValueForOption(pidOption),
            MemoryType = bindingContext.ParseResult.GetValueForOption(memoryOption),
            MaxPayloadSize = bindingContext.ParseResult.GetValueForOption(maxPayloadOption),
            Slot = bindingContext.ParseResult.GetValueForOption(slotOption)
        };
    }
}