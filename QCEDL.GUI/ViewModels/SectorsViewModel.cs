using System.Diagnostics;
using System.Reactive.Linq;
using Avalonia.Threading;
using QCEDL.GUI.Services;
using Qualcomm.EmergencyDownload.Helpers;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace QCEDL.GUI.ViewModels;

public sealed partial class SectorsViewModel : ViewModelBase
{
    private readonly EdlService _service;
    private readonly IObservable<bool> _canRun;

    [Reactive] private uint _lun;
    [Reactive] private ulong _startLba;
    [Reactive] private ulong _sectorCount = 1;
    [Reactive] private string _status = string.Empty;

    private long _bytesDone;
    private long _bytesTotal;
    private bool _isBusy;

    public SectorsViewModel(EdlService service)
    {
        _service = service;
        _canRun = this.WhenAnyValue(x => x.CanInteract);

        LogCommandErrors();

        Localizer.Instance.CultureChanged += (_, _) =>
            this.RaisePropertyChanged(nameof(ProgressText));
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isBusy, value);
            this.RaisePropertyChanged(nameof(CanInteract));
        }
    }

    public bool CanInteract => !_isBusy;

    public long BytesDone
    {
        get => _bytesDone;
        private set
        {
            this.RaiseAndSetIfChanged(ref _bytesDone, value);
            this.RaisePropertyChanged(nameof(ProgressPercent));
            this.RaisePropertyChanged(nameof(ProgressText));
        }
    }

    public long BytesTotal
    {
        get => _bytesTotal;
        private set
        {
            this.RaiseAndSetIfChanged(ref _bytesTotal, value);
            this.RaisePropertyChanged(nameof(ProgressPercent));
            this.RaisePropertyChanged(nameof(ProgressText));
        }
    }

    public double ProgressPercent => _bytesTotal > 0
        ? Math.Min(100.0, 100.0 * _bytesDone / _bytesTotal)
        : 0;

    public string ProgressText => _bytesTotal <= 0
        ? string.Empty
        : Localizer.Instance.Format(
            "Progress_Format",
            _bytesDone / (1024.0 * 1024.0),
            _bytesTotal / (1024.0 * 1024.0),
            ProgressPercent);

    public Interaction<SaveFileRequest, string?> PickSaveFile { get; } = new();
    public Interaction<OpenFileRequest, IReadOnlyList<string>> PickOpenFile { get; } = new();
    public Interaction<ConfirmRequest, bool> Confirm { get; } = new();

    private static long ClampToLong(ulong value) =>
        value > long.MaxValue ? long.MaxValue : (long)value;

    private async Task<string?> PickSaveAsync(string suggested) =>
        await PickSaveFile.Handle(new SaveFileRequest(
            Localizer.Instance["Sectors_SaveTitle"],
            SuggestedName: suggested,
            DefaultExtension: "img"));

    [ReactiveCommand(CanExecute = nameof(_canRun))]
    private async Task ReadSectorsAsync()
    {
        var path = await PickSaveAsync($"lun{Lun}_lba{StartLba}.img");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        await RunAsync("Sectors_StatusReadingFormat", async () =>
        {
            var lun = Lun;
            var start = StartLba;
            var count = SectorCount;

            var geometry = await _service.GetGeometryAsync(lun);
            ResetProgress(ClampToLong(count * geometry.SectorSize));

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await using var fs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
            await _service.RunExclusiveAsync(m => m.ReadSectorsToStreamAsync(
                lun, start, count, fs, Report));

            return count;
        });
    }

    [ReactiveCommand(CanExecute = nameof(_canRun))]
    private async Task ReadLunAsync()
    {
        var path = await PickSaveAsync($"lun{Lun}.img");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        await RunAsync("Sectors_StatusReadingFormat", async () =>
        {
            var lun = Lun;
            var geometry = await _service.GetGeometryAsync(lun);
            if (geometry.TotalSectors is null or 0)
            {
                throw new InvalidOperationException("Device did not report a total block count for this LUN.");
            }

            var count = geometry.TotalSectors.Value;
            ResetProgress(ClampToLong(count * geometry.SectorSize));

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await using var fs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
            await _service.RunExclusiveAsync(m => m.ReadSectorsToStreamAsync(
                lun, 0, count, fs, Report));

            return count;
        });
    }

    [ReactiveCommand(CanExecute = nameof(_canRun))]
    private async Task WriteLunAsync()
    {
        var picked = await PickOpenFile.Handle(new OpenFileRequest(
            Localizer.Instance["Sectors_OpenTitle"],
            [FilePickerTypes.AnyFile],
            AllowMultiple: false));
        var inputPath = picked.Count > 0 ? picked[0] : null;
        if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath))
        {
            return;
        }

        var info = new FileInfo(inputPath);
        if (info.Length == 0)
        {
            Status = Localizer.Instance.Format("Sectors_ErrorFormat", "Input file is empty.");
            return;
        }

        var lunForCheck = Lun;
        var geometry = await _service.GetGeometryAsync(lunForCheck);
        if (geometry.TotalSectors is null or 0)
        {
            Status = Localizer.Instance.Format("Sectors_ErrorFormat",
                "Device did not report a total block count for this LUN.");
            return;
        }

        var lunBytes = geometry.TotalSectors.Value * geometry.SectorSize;
        if ((ulong)info.Length > lunBytes)
        {
            Status = Localizer.Instance.Format("Sectors_ErrorFormat",
                Localizer.Instance.Format("Sectors_WriteLunTooLargeFormat", info.Length, lunBytes));
            return;
        }

        var confirmed = await Confirm.Handle(new ConfirmRequest(
            Localizer.Instance["Sectors_ConfirmWriteLunTitle"],
            Localizer.Instance.Format("Sectors_ConfirmWriteLunMessageFormat", info.FullName, info.Length, lunForCheck, lunBytes),
            Danger: true,
            RequiredConfirmation: "WRITE"));
        if (!confirmed)
        {
            return;
        }

        await RunAsync("Sectors_StatusWritingFormat", async () =>
        {
            var lun = lunForCheck;
            ResetProgress(info.Length);

            await _service.RunExclusiveAsync(async m =>
            {
                await using var stream = info.OpenRead();
                await m.WriteSectorsFromStreamAsync(
                    lun, 0, stream, stream.Length, padToSector: true, info.Name, Report);
            });

            return (ulong)info.Length;
        });
    }

    [ReactiveCommand(CanExecute = nameof(_canRun))]
    private async Task WriteSectorsAsync()
    {
        var picked = await PickOpenFile.Handle(new OpenFileRequest(
            Localizer.Instance["Sectors_OpenTitle"],
            [FilePickerTypes.AnyFile],
            AllowMultiple: false));
        var inputPath = picked.Count > 0 ? picked[0] : null;
        if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath))
        {
            return;
        }

        var info = new FileInfo(inputPath);
        var confirmed = await Confirm.Handle(new ConfirmRequest(
            Localizer.Instance["Sectors_ConfirmWriteTitle"],
            Localizer.Instance.Format("Sectors_ConfirmWriteMessageFormat", info.FullName, info.Length, Lun, StartLba),
            Danger: true,
            RequiredConfirmation: "WRITE"));
        if (!confirmed)
        {
            return;
        }

        await RunAsync("Sectors_StatusWritingFormat", async () =>
        {
            var lun = Lun;
            var start = StartLba;
            ResetProgress(info.Length);

            await _service.RunExclusiveAsync(async m =>
            {
                await using var stream = info.OpenRead();
                await m.WriteSectorsFromStreamAsync(
                    lun, start, stream, stream.Length, padToSector: true, info.Name, Report)
                    ;
            });

            return (ulong)info.Length;
        });
    }

    [ReactiveCommand(CanExecute = nameof(_canRun))]
    private async Task EraseSectorsAsync()
    {
        var confirmed = await Confirm.Handle(new ConfirmRequest(
            Localizer.Instance["Sectors_ConfirmEraseTitle"],
            Localizer.Instance.Format("Sectors_ConfirmEraseMessageFormat", Lun, StartLba, SectorCount),
            Danger: true,
            RequiredConfirmation: "ERASE"));
        if (!confirmed)
        {
            return;
        }

        await RunAsync("Sectors_StatusErasingFormat", async () =>
        {
            var lun = Lun;
            var start = StartLba;
            var count = SectorCount;

            var geometry = await _service.GetGeometryAsync(lun);
            ResetProgress(ClampToLong(count * geometry.SectorSize));

            await _service.RunExclusiveAsync(m => m.EraseSectorsAsync(lun, start, count, Report))
                ;

            return count;
        });
    }

    private void ResetProgress(long total) => Dispatcher.UIThread.Post(() =>
    {
        BytesDone = 0;
        BytesTotal = total;
    });

    private void Report(long done, long total) => Dispatcher.UIThread.Post(() =>
    {
        BytesDone = done;
        if (total > 0)
        {
            BytesTotal = total;
        }
    });

    private async Task RunAsync(string statusKey, Func<Task<ulong>> body)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        Status = Localizer.Instance.Format(statusKey, Lun, StartLba, SectorCount);
        var sw = Stopwatch.StartNew();
        try
        {
            var count = await body();
            sw.Stop();
            Status = Localizer.Instance.Format("Sectors_StatusDoneFormat", count, sw.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Status = Localizer.Instance.Format("Sectors_ErrorFormat", ex.Message);
            Logging.Log($"Sectors op failed: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }
}