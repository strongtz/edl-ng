using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Reactive.Linq;
using Avalonia.Threading;
using QCEDL.GUI.Services;
using QCEDL.NET.PartitionTable;
using Qualcomm.EmergencyDownload.Helpers;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace QCEDL.GUI.ViewModels;

public sealed partial class PartitionsViewModel : ViewModelBase
{
    private readonly EdlService _service;
    private readonly IObservable<bool> _canRun;

    [Reactive] private string? _lunFilter;
    [Reactive] private string? _opPartitionName;
    [Reactive] private string? _opLun;
    [Reactive] private string _opStatus = string.Empty;

    private bool _isBusy;
    private bool _isOpRunning;
    private string _statusKey = "Parts_StatusNotScanned";
    private object?[] _statusArgs = [];
    private long _opBytesDone;
    private long _opBytesTotal;
    private PartitionRow? _selectedRow;

    public PartitionsViewModel(EdlService service)
    {
        _service = service;
        _canRun = this.WhenAnyValue(x => x.CanInteract);

        ScanCommand.ThrownExceptions.Subscribe(ex =>
            Logging.Log(Localizer.Instance.Format("Parts_LogScanFailedFormat", ex.Message), LogLevel.Error));

        LogCommandErrors();

        Localizer.Instance.CultureChanged += (_, _) =>
        {
            this.RaisePropertyChanged(nameof(StatusText));
            this.RaisePropertyChanged(nameof(ProgressText));
        };
    }

    public ObservableCollection<PartitionRow> Partitions { get; } = [];

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isBusy, value);
            this.RaisePropertyChanged(nameof(CanInteract));
        }
    }

    public bool IsOpRunning
    {
        get => _isOpRunning;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isOpRunning, value);
            this.RaisePropertyChanged(nameof(CanInteract));
        }
    }

    public bool CanInteract => !_isBusy && !_isOpRunning;

    public string StatusText => _statusArgs.Length == 0
        ? Localizer.Instance[_statusKey]
        : Localizer.Instance.Format(_statusKey, _statusArgs);

    public long OpBytesDone
    {
        get => _opBytesDone;
        private set
        {
            this.RaiseAndSetIfChanged(ref _opBytesDone, value);
            this.RaisePropertyChanged(nameof(ProgressPercent));
            this.RaisePropertyChanged(nameof(ProgressText));
        }
    }

    public long OpBytesTotal
    {
        get => _opBytesTotal;
        private set
        {
            this.RaiseAndSetIfChanged(ref _opBytesTotal, value);
            this.RaisePropertyChanged(nameof(ProgressPercent));
            this.RaisePropertyChanged(nameof(ProgressText));
        }
    }

    public double ProgressPercent => _opBytesTotal > 0
        ? Math.Min(100.0, 100.0 * _opBytesDone / _opBytesTotal)
        : 0;

    public string ProgressText => _opBytesTotal <= 0
        ? string.Empty
        : Localizer.Instance.Format(
            "Progress_Format",
            _opBytesDone / (1024.0 * 1024.0),
            _opBytesTotal / (1024.0 * 1024.0),
            ProgressPercent);

    public PartitionRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedRow, value);
            if (value is not null)
            {
                OpPartitionName = value.Name;
                OpLun = value.Lun.ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    public Interaction<SaveFileRequest, string?> PickSaveFile { get; } = new();
    public Interaction<OpenFileRequest, IReadOnlyList<string>> PickOpenFile { get; } = new();
    public Interaction<ConfirmRequest, bool> Confirm { get; } = new();

    private void SetStatus(string key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        this.RaisePropertyChanged(nameof(StatusText));
    }

    private static uint? ParseLun(string? s) =>
        !string.IsNullOrWhiteSpace(s) && uint.TryParse(s.Trim(), out var v) ? v : null;

    [ReactiveCommand(CanExecute = nameof(_canRun))]
    private async Task ScanAsync()
    {
        if (IsBusy || IsOpRunning)
        {
            return;
        }

        IsBusy = true;
        Partitions.Clear();
        try
        {
            var lunParam = ParseLun(LunFilter);

            await _service.RunExclusiveAsync(async m =>
            {
                var luns = await m.DetermineLunsToScanAsync(lunParam);
                foreach (var lun in luns)
                {
                    try
                    {
                        var effectiveLun = m.IsDirectMode ? 0u : lun;
                        var geometry = await m.GetStorageGeometryAsync(effectiveLun);
                        var data = await m.ReadSectorsAsync(effectiveLun, 0, 64);
                        using var stream = new MemoryStream(data);
                        var gpt = Gpt.ReadFromStream(stream, (int)geometry.SectorSize);
                        if (gpt is null)
                        {
                            Logging.Log(Localizer.Instance.Format("Parts_LogNoGptFormat", lun), LogLevel.Warning);
                            continue;
                        }

                        for (var i = 0; i < gpt.Partitions.Count; i++)
                        {
                            var p = gpt.Partitions[i];
                            if (p.FirstLBA == 0 && p.LastLBA == 0)
                            {
                                continue;
                            }
                            var row = PartitionRow.From(p, effectiveLun, i, geometry.SectorSize);
                            await Dispatcher.UIThread.InvokeAsync(() => Partitions.Add(row));
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.Log(Localizer.Instance.Format("Parts_LogScanErrorFormat", lun, ex.Message), LogLevel.Warning);
                    }

                    if (m.IsDirectMode)
                    {
                        break;
                    }
                }
            });

            SetStatus("Parts_StatusFoundFormat", Partitions.Count);
        }
        catch (Exception ex)
        {
            Logging.Log(Localizer.Instance.Format("Parts_LogScanFailedFormat", ex.Message), LogLevel.Error);
            SetStatus("Parts_ErrorFormat", ex.Message);
        }
        finally { IsBusy = false; }
    }

    // ─── Operations ───────────────────────────────────────────────────────────

    private static long ClampToLong(ulong value) => value > long.MaxValue ? long.MaxValue : (long)value;

    [ReactiveCommand(CanExecute = nameof(_canRun))]
    private async Task ReadPartitionAsync()
    {
        var name = OpPartitionName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var savePath = await PickSaveFile.Handle(new SaveFileRequest(
            Localizer.Instance["Parts_Ops_SavePickerTitle"],
            SuggestedName: $"{name}.img",
            DefaultExtension: "img"));
        if (string.IsNullOrEmpty(savePath))
        {
            return;
        }

        await RunOpAsync("Parts_Ops_StatusReadingFormat", name, async () =>
        {
            var lun = ParseLun(OpLun);
            var found = await _service.RunExclusiveAsync(m => m.FindPartitionWithLunAsync(name, lun))
                 ?? throw new InvalidOperationException(
                    Localizer.Instance.Format("Parts_Ops_PartitionNotFoundFormat", name));

            var (partition, actualLun) = found;
            var sectorCount = partition.LastLBA - partition.FirstLBA + 1;
            var geometry = await _service.GetGeometryAsync(actualLun);
            ResetProgress(ClampToLong(sectorCount * geometry.SectorSize));

            var dir = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await using var file = File.Open(savePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await _service.RunExclusiveAsync(m => m.ReadSectorsToStreamAsync(
                actualLun, partition.FirstLBA, sectorCount, file, ReportProgress))
                ;
        });
    }

    [ReactiveCommand(CanExecute = nameof(_canRun))]
    private async Task WritePartitionAsync()
    {
        var name = OpPartitionName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var picked = await PickOpenFile.Handle(new OpenFileRequest(
            Localizer.Instance["Parts_Ops_OpenPickerTitle"],
            [FilePickerTypes.AnyFile],
            AllowMultiple: false));
        var inputPath = picked.Count > 0 ? picked[0] : null;
        if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath))
        {
            return;
        }

        var info = new FileInfo(inputPath);
        var confirmed = await Confirm.Handle(new ConfirmRequest(
            Localizer.Instance["Parts_Ops_ConfirmWriteTitle"],
            Localizer.Instance.Format("Parts_Ops_ConfirmWriteMessageFormat", name, info.FullName, info.Length),
            Danger: true));
        if (!confirmed)
        {
            return;
        }

        await RunOpAsync("Parts_Ops_StatusWritingFormat", name, async () =>
        {
            var lun = ParseLun(OpLun);
            var found = await _service.RunExclusiveAsync(m => m.FindPartitionWithLunAsync(name, lun))
                 ?? throw new InvalidOperationException(
                    Localizer.Instance.Format("Parts_Ops_PartitionNotFoundFormat", name));

            var (partition, actualLun) = found;
            var geometry = await _service.GetGeometryAsync(actualLun);
            var partitionBytes = (partition.LastLBA - partition.FirstLBA + 1) * geometry.SectorSize;
            if ((ulong)info.Length > partitionBytes)
            {
                throw new InvalidOperationException(
                    $"Input file ({info.Length} bytes) exceeds partition size ({partitionBytes} bytes).");
            }

            ResetProgress(info.Length);

            await _service.RunExclusiveAsync(async m =>
            {
                await using var stream = info.OpenRead();
                await m.WriteSectorsFromStreamAsync(
                    actualLun,
                    partition.FirstLBA,
                    stream,
                    stream.Length,
                    padToSector: true,
                    info.Name,
                    ReportProgress);
            });
        });
    }

    [ReactiveCommand(CanExecute = nameof(_canRun))]
    private async Task ErasePartitionAsync()
    {
        var name = OpPartitionName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var confirmed = await Confirm.Handle(new ConfirmRequest(
            Localizer.Instance["Parts_Ops_ConfirmEraseTitle"],
            Localizer.Instance.Format("Parts_Ops_ConfirmEraseMessageFormat", name),
            Danger: true,
            RequiredConfirmation: name));
        if (!confirmed)
        {
            return;
        }

        await RunOpAsync("Parts_Ops_StatusErasingFormat", name, async () =>
        {
            var lun = ParseLun(OpLun);
            var found = await _service.RunExclusiveAsync(m => m.FindPartitionWithLunAsync(name, lun))
                 ?? throw new InvalidOperationException(
                    Localizer.Instance.Format("Parts_Ops_PartitionNotFoundFormat", name));

            var (partition, actualLun) = found;
            var sectorCount = partition.LastLBA - partition.FirstLBA + 1;
            var geometry = await _service.GetGeometryAsync(actualLun);
            ResetProgress(ClampToLong(sectorCount * geometry.SectorSize));

            await _service.RunExclusiveAsync(m => m.EraseSectorsAsync(
                actualLun, partition.FirstLBA, sectorCount, ReportProgress))
                ;
        });
    }

    private void ResetProgress(long total)
    {
        Dispatcher.UIThread.Post(() =>
        {
            OpBytesDone = 0;
            OpBytesTotal = total;
        });
    }

    private void ReportProgress(long done, long total)
    {
        Dispatcher.UIThread.Post(() =>
        {
            OpBytesDone = done;
            if (total > 0)
            {
                OpBytesTotal = total;
            }
        });
    }

    private async Task RunOpAsync(string statusFormatKey, string label, Func<Task> body)
    {
        if (IsBusy || IsOpRunning)
        {
            return;
        }

        IsOpRunning = true;
        OpStatus = Localizer.Instance.Format(statusFormatKey, label);
        var sw = Stopwatch.StartNew();
        try
        {
            await body();
            sw.Stop();
            OpStatus = Localizer.Instance.Format("Parts_Ops_StatusDoneFormat", label, sw.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            OpStatus = Localizer.Instance.Format("Parts_Ops_StatusFailedFormat", ex.Message);
            Logging.Log($"Partition op failed: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsOpRunning = false;
        }
    }
}