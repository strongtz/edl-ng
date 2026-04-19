using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using QCEDL.GUI.Services;
using Qualcomm.EmergencyDownload.Core;
using Qualcomm.EmergencyDownload.Helpers;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace QCEDL.GUI.ViewModels;

public sealed partial class ConnectionViewModel : ViewModelBase
{
    private readonly EdlService _service;
    private readonly IObservable<bool> _canRun;
    private readonly IObservable<bool> _canProbe;

    // Form fields — direct two-way bound.
    [Reactive] private string? _loaderPath;
    [Reactive] private string _vidHex;
    [Reactive] private string _pidHex;
    [Reactive] private StorageType _memoryType;
    [Reactive] private uint _slot;
    [Reactive] private string? _hostDevTarget;
    [Reactive] private string? _imgSize;
    [Reactive] private bool _radxaWos;
    [Reactive] private bool _directEnabled;
    [Reactive] private string? _serialDevicePath;
    [Reactive] private TransportBackend _backend;

    // Session state fed by the commands.
    [Reactive] private bool _isBusy;
    [Reactive] private DeviceCandidate? _selectedCandidate;
    [Reactive] private string _statusKey = "Conn_StatusNotConnected";
    [Reactive] private object?[] _statusArgs = [];
    [Reactive] private string _modeKey = "Conn_ModeUnknown";
    [Reactive] private string? _modeLiteral;
    [Reactive] private string _devicesStatusKey = "Conn_DevicesEmpty";

    // ObservableAsProperty backing — all derived values flow through here.
    private readonly ObservableAsPropertyHelper<bool> _isSerialBackend;
    private readonly ObservableAsPropertyHelper<bool> _isUsbBackend;
    private readonly ObservableAsPropertyHelper<bool> _isDirectMode;
    private readonly ObservableAsPropertyHelper<bool> _isDeviceListVisible;
    private readonly ObservableAsPropertyHelper<bool> _hasCandidates;
    private readonly ObservableAsPropertyHelper<bool> _hasNoCandidates;
    private readonly ObservableAsPropertyHelper<string> _statusText;
    private readonly ObservableAsPropertyHelper<string> _modeText;
    private readonly ObservableAsPropertyHelper<string> _devicesStatusText;

    public ConnectionViewModel(EdlService service)
    {
        _service = service;
        _canRun = this.WhenAnyValue(x => x.IsBusy).Select(b => !b);

        MemoryTypes = Enum.GetValues<StorageType>();
        Backends = Enum.GetValues<TransportBackend>();

        // Seed from persisted values so the user's last target stays around across launches.
        var settings = GuiSettings.Current;
        _loaderPath = settings.LoaderPath;
        _vidHex = string.IsNullOrWhiteSpace(settings.VidHex) ? "05C6" : settings.VidHex;
        _pidHex = string.IsNullOrWhiteSpace(settings.PidHex) ? "9008" : settings.PidHex;
        _memoryType = Enum.TryParse<StorageType>(settings.MemoryType, ignoreCase: true, out var mt) ? mt : StorageType.Ufs;
        _backend = Enum.TryParse<TransportBackend>(settings.Backend, ignoreCase: true, out var be) ? be : TransportBackend.Auto;

        // ── Event sources ──────────────────────────────────────────────────
        var cultureChanges = Observable
            .FromEventPattern(
                h => Localizer.Instance.CultureChanged += h,
                h => Localizer.Instance.CultureChanged -= h)
            .Select(_ => Unit.Default)
            .StartWith(Unit.Default);

        var candidateChanges = Observable
            .FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                h => Candidates.CollectionChanged += h,
                h => Candidates.CollectionChanged -= h)
            .Select(_ => Unit.Default)
            .StartWith(Unit.Default);

        // ── Derived properties (ObservableAsProperty) ──────────────────────
        _isSerialBackend = this.WhenAnyValue(x => x.Backend)
            .Select(b => b == TransportBackend.Serial)
            .ToProperty(this, nameof(IsSerialBackend));

        _isUsbBackend = this.WhenAnyValue(x => x.Backend)
            .Select(b => b != TransportBackend.Serial)
            .ToProperty(this, nameof(IsUsbBackend));

        _isDirectMode = this
            .WhenAnyValue(x => x.DirectEnabled, x => x.HostDevTarget, x => x.RadxaWos,
                (enabled, h, r) => enabled && (!string.IsNullOrWhiteSpace(h) || (r && IsWindows)))
            .ToProperty(this, nameof(IsDirectMode));

        _isDeviceListVisible = this.WhenAnyValue(x => x.IsDirectMode)
            .Select(d => !d)
            .ToProperty(this, nameof(IsDeviceListVisible));

        _hasCandidates = candidateChanges
            .Select(_ => Candidates.Count > 0)
            .ToProperty(this, nameof(HasCandidates));

        _hasNoCandidates = candidateChanges
            .Select(_ => Candidates.Count == 0)
            .ToProperty(this, nameof(HasNoCandidates));

        _statusText = cultureChanges
            .CombineLatest(this.WhenAnyValue(x => x.StatusKey, x => x.StatusArgs),
                (_, s) => (s.Item1, s.Item2))
            .Select(s => s.Item2.Length == 0
                ? Localizer.Instance[s.Item1]
                : Localizer.Instance.Format(s.Item1, s.Item2))
            .ToProperty(this, nameof(StatusText));

        _modeText = cultureChanges
            .CombineLatest(this.WhenAnyValue(x => x.ModeKey, x => x.ModeLiteral),
                (_, s) => (s.Item1, s.Item2))
            .Select(s => s.Item2 ?? Localizer.Instance[s.Item1])
            .ToProperty(this, nameof(ModeText));

        _devicesStatusText = Observable
            .Merge(
                cultureChanges,
                candidateChanges,
                this.WhenAnyValue(x => x.DevicesStatusKey).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.SelectedCandidate).Select(_ => Unit.Default))
            .Select(_ => ComputeDevicesStatusText())
            .ToProperty(this, nameof(DevicesStatusText));

        // ── Command gates ──────────────────────────────────────────────────
        // Probe normally requires Step 1 (Scan) to have populated a candidate list,
        // but a manually typed serial-device path (honoured by EdlManager directly) or
        // direct-mode fields also unblock the command — discovery is not the only entry.
        _canProbe = this.WhenAnyValue(
            x => x.IsBusy, x => x.IsDirectMode, x => x.HasCandidates, x => x.SerialDevicePath,
            (busy, direct, hasCandidates, serialPath) =>
                !busy && (direct || hasCandidates || !string.IsNullOrWhiteSpace(serialPath)));

        // ── Side effects: selection drives the status-key ──────────────────
        this.WhenAnyValue(x => x.SelectedCandidate)
            .Subscribe(c => DevicesStatusKey = c is not null
                ? "Conn_DeviceSelectedFormat"
                : Candidates.Count == 0 ? "Conn_DevicesEmpty" : "Conn_DevicesFoundFormat");

        // Keep EdlService.Options in sync with the form so downstream views (Partitions,
        // Sectors, RawProgram, Advanced) pick up the user's target without first needing a
        // Probe. Any change resets the live session so the next op re-opens with fresh options.
        // Each WhenAnyValue emits its seed immediately; Skip(1) drops those so we only fire
        // on real user edits.
        Observable.Merge(
                this.WhenAnyValue(x => x.LoaderPath).Skip(1).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.VidHex).Skip(1).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.PidHex).Skip(1).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.MemoryType).Skip(1).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.Slot).Skip(1).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.HostDevTarget).Skip(1).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.ImgSize).Skip(1).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.RadxaWos).Skip(1).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.DirectEnabled).Skip(1).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.SerialDevicePath).Skip(1).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.Backend).Skip(1).Select(_ => Unit.Default),
                this.WhenAnyValue(x => x.SelectedCandidate).Skip(1).Select(_ => Unit.Default))
            .Subscribe(_ => ProjectOptions(resetSession: true));

        // Seed the service with the initial form state so ops work before any edits.
        ProjectOptions(resetSession: true);

        // Localized wording for the probe hot path: keep the explicit hook so the Conn_* key drives the message.
        ProbeCommand.ThrownExceptions.Subscribe(ex =>
        {
            Logging.Log(Localizer.Instance.Format("Conn_ProbeFailedFormat", ex.Message), LogLevel.Error);
            LogElevationHintFor(ex);
        });

        LogCommandErrors();
    }

    public IReadOnlyList<StorageType> MemoryTypes { get; }
    public IReadOnlyList<TransportBackend> Backends { get; }

    public ObservableCollection<DeviceCandidate> Candidates { get; } = [];

    public bool IsWindows { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public bool IsUnix { get; } = !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public bool IsSerialBackend => _isSerialBackend.Value;
    public bool IsUsbBackend => _isUsbBackend.Value;

    /// <summary>True when the user has asked for a direct-mode backend (host block device or
    /// Radxa WoS), in which case USB device enumeration is irrelevant.</summary>
    public bool IsDirectMode => _isDirectMode.Value;

    /// <summary>The candidate list + Scan button should be hidden when we are in direct mode.</summary>
    public bool IsDeviceListVisible => _isDeviceListVisible.Value;

    public bool HasCandidates => _hasCandidates.Value;
    public bool HasNoCandidates => _hasNoCandidates.Value;

    public string StatusText => _statusText.Value;
    public string ModeText => _modeText.Value;
    public string DevicesStatusText => _devicesStatusText.Value;

    public Interaction<OpenFileRequest, IReadOnlyList<string>> PickFile { get; } = new();
    public Interaction<DeviceChooserRequest, DeviceCandidate?> PickDevice { get; } = new();

    private string ComputeDevicesStatusText()
    {
        var key = DevicesStatusKey;
        return key == "Conn_DevicesFoundFormat"
            ? Localizer.Instance.Format(key, Candidates.Count)
            : key == "Conn_DeviceSelectedFormat" && SelectedCandidate is not null
                ? Localizer.Instance.Format(key, SelectedCandidate.DisplayName)
                : Localizer.Instance[key];
    }

    /// <summary>
    /// Projects the current form fields onto <see cref="EdlService.Options"/> and persists
    /// them to <see cref="GuiSettings"/>. When <paramref name="resetSession"/> is true the
    /// live session is torn down so the next call uses the new options — required before
    /// Connect/Probe. Scan leaves the session intact (<paramref name="resetSession"/> = false).
    /// </summary>
    private void ProjectOptions(bool resetSession)
    {
        var selectedSerialPath = SelectedCandidate is { Backend: TransportBackend.Serial } serialPick
            ? serialPick.Id
            : null;
        var selectedUsbId = SelectedCandidate is { Backend: TransportBackend.LibUsb } usbPick
            ? usbPick.Id
            : null;

        var effectiveSerialPath = selectedSerialPath ??
            (string.IsNullOrWhiteSpace(SerialDevicePath) ? null : SerialDevicePath);

        _service.Options = new EdlOptions
        {
            LoaderPath = LoaderPath,
            Vid = TryParseHex(VidHex),
            Pid = TryParseHex(PidHex),
            MemoryType = MemoryType,
            LogLevel = Logging.CurrentLogLevel,
            Slot = Slot,
            HostDevAsTarget = DirectEnabled && !string.IsNullOrWhiteSpace(HostDevTarget) ? HostDevTarget : null,
            ImgSize = DirectEnabled && !string.IsNullOrWhiteSpace(ImgSize) ? ImgSize : null,
            RadxaWosPlatform = DirectEnabled && RadxaWos && IsWindows,
            Backend = Backend,
            SerialDevicePath = Backend == TransportBackend.Serial ? effectiveSerialPath : null,
            UsbDeviceId = Backend != TransportBackend.Serial ? selectedUsbId : null,
        };

        if (resetSession)
        {
            _service.ResetSession();
        }

        // Persist last-used options so the next launch defaults to what was actually tried.
        var prefs = GuiSettings.Current;
        prefs.LoaderPath = LoaderPath;
        prefs.VidHex = VidHex;
        prefs.PidHex = PidHex;
        prefs.MemoryType = MemoryType.ToString();
        prefs.Backend = Backend.ToString();
        GuiSettings.Save();
    }

    [ReactiveCommand(CanExecute = nameof(_canProbe))]
    private async Task ProbeAsync()
    {
        IsBusy = true;
        try
        {
            ProjectOptions(resetSession: true);
            if (!await EnsureDeviceSelectionAsync())
            {
                return;
            }
            Logging.Log(Localizer.Instance["Conn_LogProbing"], LogLevel.Info);
            var mode = await _service.ProbeAsync();
            ModeLiteral = mode.ToString();
            StatusKey = "Conn_StatusDetectedFormat";
            StatusArgs = [mode];
        }
        finally { IsBusy = false; }
    }

    [ReactiveCommand(CanExecute = nameof(_canRun))]
    private async Task ScanDevicesAsync()
    {
        if (IsDirectMode)
        {
            // No USB to enumerate — clear any stale candidates and tell the user.
            Candidates.Clear();
            SelectedCandidate = null;
            Logging.Log(Localizer.Instance["Conn_LogDirectSession"], LogLevel.Info);
            return;
        }

        IsBusy = true;
        try
        {
            // Non-destructive: scan must not tear down an active session.
            ProjectOptions(resetSession: false);

            var previousId = SelectedCandidate?.Id;
            var list = await _service.EnumerateDevicesAsync();
            ReplaceCandidates(list);

            // Try to preserve the previous pin; if it's gone, auto-select when exactly one
            // candidate is present so the user never has to click through a trivial list.
            SelectedCandidate = previousId is not null
                ? Candidates.FirstOrDefault(c => c.Id == previousId)
                : Candidates.Count == 1 ? Candidates[0] : null;

            // When auto-selection didn't happen, flip the status line to the "N found" form
            // so users see the count even without a pin.
            if (SelectedCandidate is null)
            {
                DevicesStatusKey = Candidates.Count == 0 ? "Conn_DevicesNoneFound" : "Conn_DevicesFoundFormat";
            }

            Logging.Log(
                Localizer.Instance.Format("Conn_DevicesFoundFormat", Candidates.Count),
                LogLevel.Info);
        }
        finally { IsBusy = false; }
    }

    [ReactiveCommand]
    private void ClearDeviceSelection()
    {
        SelectedCandidate = null;
    }

    /// <summary>
    /// Resolve the device we'll connect to: prefer the user's current pin, fall back to the
    /// already-scanned candidate list, and only enumerate if we have nothing cached. When
    /// multiple matches exist and none is pinned, open the picker dialog. Returns false if
    /// the user cancels the picker.
    /// </summary>
    private async Task<bool> EnsureDeviceSelectionAsync()
    {
        if (SelectedCandidate is not null)
        {
            return true;
        }

        IReadOnlyList<DeviceCandidate> list;
        if (Candidates.Count > 0)
        {
            list = [.. Candidates];
        }
        else
        {
            list = await _service.EnumerateDevicesAsync();
            if (list.Count > 0)
            {
                ReplaceCandidates(list);
            }
        }

        if (list.Count == 0)
        {
            // Nothing found: let the existing discovery path log "not found".
            return true;
        }

        if (list.Count == 1)
        {
            SelectedCandidate = Candidates.Count > 0 ? Candidates[0] : list[0];
            return true;
        }

        Logging.Log(Localizer.Instance["Conn_LogMultipleFoundPrompt"], LogLevel.Info);
        var picked = await PickDevice.Handle(new DeviceChooserRequest(list));
        if (picked is null)
        {
            Logging.Log(Localizer.Instance["Conn_LogSelectionCancelled"], LogLevel.Warning);
            return false;
        }

        SelectedCandidate = Candidates.FirstOrDefault(c => c.Id == picked.Id) ?? picked;
        // The pin changed after ProjectOptions already ran, so re-project (no session reset
        // needed — the session was torn down moments ago and is still torn down).
        ProjectOptions(resetSession: false);
        return true;
    }

    private void ReplaceCandidates(IReadOnlyList<DeviceCandidate> list)
    {
        Candidates.Clear();
        foreach (var c in list)
        {
            Candidates.Add(c);
        }
    }

    [ReactiveCommand]
    private async Task BrowseLoaderAsync()
    {
        var paths = await PickFile.Handle(new OpenFileRequest(
            Localizer.Instance["Conn_LoaderPickTitle"],
            [FilePickerTypes.FirehoseLoader, FilePickerTypes.AnyFile],
            AllowMultiple: false));
        if (paths.Count > 0 && !string.IsNullOrEmpty(paths[0]))
        {
            LoaderPath = paths[0];
        }
    }

    private static int? TryParseHex(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }
        var trimmed = s.Trim().Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase);
        return int.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : null;
    }

    /// <summary>
    /// When a connect/probe failure looks like a permission problem on Linux, point the
    /// user at the shipped udev rule. Windows needs no elevation hint.
    /// </summary>
    private static void LogElevationHintFor(Exception ex)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }
        var message = ex.Message ?? string.Empty;
        if (ex is UnauthorizedAccessException ||
            message.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ||
            (message.Contains("Access", StringComparison.OrdinalIgnoreCase) && message.Contains("denied", StringComparison.OrdinalIgnoreCase)) ||
            message.Contains("LIBUSB_ERROR_ACCESS", StringComparison.OrdinalIgnoreCase))
        {
            Logging.Log(Localizer.Instance["Conn_LogElevationLinuxHint"], LogLevel.Warning);
        }
    }
}