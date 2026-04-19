using System.Diagnostics;
using System.Globalization;
using System.Reactive.Linq;
using QCEDL.GUI.Services;
using Qualcomm.EmergencyDownload.Core;
using Qualcomm.EmergencyDownload.Helpers;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace QCEDL.GUI.ViewModels;

public sealed partial class AdvancedViewModel : ViewModelBase
{
    private readonly EdlService _service;
    private readonly IObservable<bool> _canRun;
    private readonly IObservable<bool> _canUploadLoader;

    [Reactive] private string? _provisionXmlPath;
    [Reactive] private string _provisionStatus = string.Empty;
    [Reactive] private string _uploadStatus = string.Empty;
    [Reactive] private PowerValue _resetMode = PowerValue.Reset;
    [Reactive] private string _resetDelay = "1";
    [Reactive] private string _resetStatus = string.Empty;

    private bool _isProvisionBusy;
    private bool _isUploadBusy;
    private bool _isResetBusy;

    public AdvancedViewModel(EdlService service)
    {
        _service = service;
        ResetModes = Enum.GetValues<PowerValue>();
        _canRun = this.WhenAnyValue(x => x.CanInteract);
        _canUploadLoader = this.WhenAnyValue(x => x.CanInteract);

        LogCommandErrors();
    }

    public IReadOnlyList<PowerValue> ResetModes { get; }

    public bool CanInteract => !_isProvisionBusy && !_isUploadBusy && !_isResetBusy;

    public bool IsProvisionBusy
    {
        get => _isProvisionBusy;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isProvisionBusy, value);
            this.RaisePropertyChanged(nameof(CanInteract));
        }
    }

    public bool IsUploadBusy
    {
        get => _isUploadBusy;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isUploadBusy, value);
            this.RaisePropertyChanged(nameof(CanInteract));
        }
    }

    public bool IsResetBusy
    {
        get => _isResetBusy;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isResetBusy, value);
            this.RaisePropertyChanged(nameof(CanInteract));
        }
    }

    public Interaction<OpenFileRequest, IReadOnlyList<string>> PickFile { get; } = new();
    public Interaction<ConfirmRequest, bool> Confirm { get; } = new();

    [ReactiveCommand]
    private async Task BrowseProvisionAsync()
    {
        var paths = await PickFile.Handle(new OpenFileRequest(
            Localizer.Instance["Adv_ProvisionPickTitle"],
            [FilePickerTypes.Xml],
            AllowMultiple: false));
        if (paths.Count > 0 && !string.IsNullOrEmpty(paths[0]))
        {
            ProvisionXmlPath = paths[0];
        }
    }

    [ReactiveCommand(CanExecute = nameof(_canRun))]
    private async Task ProvisionAsync()
    {
        if (string.IsNullOrWhiteSpace(ProvisionXmlPath) || !File.Exists(ProvisionXmlPath))
        {
            ProvisionStatus = Localizer.Instance["Adv_ProvisionNoFile"];
            return;
        }

        var confirmed = await Confirm.Handle(new ConfirmRequest(
            Localizer.Instance["Adv_ConfirmProvisionTitle"],
            Localizer.Instance.Format("Adv_ConfirmProvisionMessageFormat", ProvisionXmlPath),
            Danger: true,
            RequiredConfirmation: "PROVISION"));
        if (!confirmed)
        {
            return;
        }

        await RunAsync(
            v => IsProvisionBusy = v,
            s => ProvisionStatus = s,
            "Adv_ProvisionRunning",
            "Adv_ProvisionDoneFormat",
            async () =>
            {
                var file = new FileInfo(ProvisionXmlPath);
                await _service.RunExclusiveAsync(m => ProvisionRunner.RunAsync(m, file));
            });
    }

    [ReactiveCommand(CanExecute = nameof(_canUploadLoader))]
    private async Task UploadLoaderAsync()
    {
        if (string.IsNullOrWhiteSpace(_service.Options.LoaderPath))
        {
            UploadStatus = Localizer.Instance["Adv_UploadNoLoader"];
            return;
        }

        await RunAsync(
            v => IsUploadBusy = v,
            s => UploadStatus = s,
            "Adv_UploadRunning",
            "Adv_UploadDoneFormat",
            () => _service.RunExclusiveAsync(async m =>
            {
                var mode = await m.DetectCurrentModeAsync();
                if (mode != DeviceMode.Sahara)
                {
                    throw new InvalidOperationException(
                        Localizer.Instance.Format("Adv_UploadWrongModeFormat", mode));
                }
                await m.UploadLoaderViaSaharaAsync();
            }));
    }

    [ReactiveCommand(CanExecute = nameof(_canRun))]
    private async Task ResetAsync()
    {
        if (!uint.TryParse(ResetDelay.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var delay))
        {
            ResetStatus = Localizer.Instance.Format("Adv_ResetBadDelayFormat", ResetDelay);
            return;
        }

        var confirmed = await Confirm.Handle(new ConfirmRequest(
            Localizer.Instance["Adv_ConfirmResetTitle"],
            Localizer.Instance.Format("Adv_ConfirmResetMessageFormat", ResetMode, delay),
            Danger: true));
        if (!confirmed)
        {
            return;
        }

        await RunAsync(
            v => IsResetBusy = v,
            s => ResetStatus = s,
            "Adv_ResetRunning",
            "Adv_ResetDoneFormat",
            () => _service.RunExclusiveAsync(async m =>
            {
                await m.EnsureFirehoseModeAsync();
                var ok = await Task.Run(() => m.Firehose.Reset(ResetMode, delay));
                if (!ok)
                {
                    throw new InvalidOperationException(
                        Localizer.Instance.Format("Adv_ResetFailedFormat", ResetMode));
                }
            }));
    }

    private async Task RunAsync(
        Action<bool> setBusy,
        Action<string> setStatus,
        string runningKey,
        string doneFormat,
        Func<Task> body)
    {
        if (!CanInteract)
        {
            return;
        }
        setBusy(true);
        setStatus(Localizer.Instance[runningKey]);
        var sw = Stopwatch.StartNew();
        try
        {
            await body();
            sw.Stop();
            setStatus(Localizer.Instance.Format(doneFormat, sw.Elapsed.TotalSeconds));
        }
        catch (Exception ex)
        {
            sw.Stop();
            setStatus(Localizer.Instance.Format("Adv_ErrorFormat", ex.Message));
            Logging.Log($"Advanced op failed: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            setBusy(false);
        }
    }
}