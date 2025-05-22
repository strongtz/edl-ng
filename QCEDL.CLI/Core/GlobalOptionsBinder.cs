using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;
using System.CommandLine;
using System.CommandLine.Binding;
using QCEDL.CLI.Helpers;

namespace QCEDL.CLI.Core;

/// <summary>
/// Binds global command line options to properties.
/// </summary>
internal sealed class GlobalOptionsBinder : BinderBase<GlobalOptionsBinder>
{
    public string? LoaderPath { get; set; }
    public int? Vid { get; set; }
    public int? Pid { get; set; }
    public StorageType? MemoryType { get; set; }
    public LogLevel LogLevel { get; set; }
    public ulong? MaxPayloadSize { get; set; }
    public uint Slot { get; set; }

    private readonly Option<FileInfo> _loaderOption;
    private readonly Option<int?> _vidOption;
    private readonly Option<int?> _pidOption;
    private readonly Option<StorageType?> _memoryOption;
    private readonly Option<LogLevel> _logLevelOption;
    private readonly Option<ulong?> _maxPayloadOption;
    private readonly Option<uint> _slotOption;

    public GlobalOptionsBinder(
        Option<FileInfo> loaderOption,
        Option<int?> vidOption,
        Option<int?> pidOption,
        Option<StorageType?> memoryOption,
        Option<LogLevel> logLevelOption,
        Option<ulong?> maxPayloadOption,
        Option<uint> slotOption)
    {
        _loaderOption = loaderOption;
        _vidOption = vidOption;
        _pidOption = pidOption;
        _memoryOption = memoryOption;
        _logLevelOption = logLevelOption;
        _maxPayloadOption = maxPayloadOption;
        _slotOption = slotOption;
    }

    protected override GlobalOptionsBinder GetBoundValue(BindingContext bindingContext)
    {
        var cliLogLevel = bindingContext.ParseResult.GetValueForOption(_logLevelOption);
        Logging.CurrentLogLevel = cliLogLevel;

        QCEDL.NET.Logging.LibraryLogger.LogAction = (message, netLogLevel, memberName, sourceFilePath, sourceLineNumber) =>
        {
            var mappedCliLevel = (LogLevel)netLogLevel;
            Logging.Log(message, mappedCliLevel);
        };

        return new GlobalOptionsBinder(_loaderOption, _vidOption, _pidOption, _memoryOption, _logLevelOption, _maxPayloadOption, _slotOption)
        {
            LoaderPath = bindingContext.ParseResult.GetValueForOption(_loaderOption)?.FullName,
            Vid = bindingContext.ParseResult.GetValueForOption(_vidOption),
            Pid = bindingContext.ParseResult.GetValueForOption(_pidOption),
            MemoryType = bindingContext.ParseResult.GetValueForOption(_memoryOption),
            LogLevel = cliLogLevel,
            MaxPayloadSize = bindingContext.ParseResult.GetValueForOption(_maxPayloadOption),
            Slot = bindingContext.ParseResult.GetValueForOption(_slotOption)
        };
    }
}