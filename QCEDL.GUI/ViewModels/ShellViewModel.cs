using System.Collections.ObjectModel;
using System.Reactive;
using QCEDL.GUI.Services;
using ReactiveUI;

namespace QCEDL.GUI.ViewModels;

public sealed class ShellViewModel : ViewModelBase
{
    private NavigationItem _selected;

    public ShellViewModel(EdlService edlService, ObservableLogSink logs)
    {
        ArgumentNullException.ThrowIfNull(edlService);
        ArgumentNullException.ThrowIfNull(logs);

        Logs = new LogsViewModel(logs);
        Connection = new ConnectionViewModel(edlService);
        Overview = new OverviewViewModel(edlService, Connection);
        Partitions = new PartitionsViewModel(edlService);
        Sectors = new SectorsViewModel(edlService);
        RawProgram = new RawProgramViewModel(edlService);
        Advanced = new AdvancedViewModel(edlService);
        Settings = new SettingsViewModel();

        NavigationItems =
        [
            new NavigationItem("Nav_Overview", Overview),
            new NavigationItem("Nav_Connection", Connection),
            new NavigationItem("Nav_Partitions", Partitions),
            new NavigationItem("Nav_Sectors", Sectors),
            new NavigationItem("Nav_RawProgram", RawProgram),
            new NavigationItem("Nav_Advanced", Advanced),
            new NavigationItem("Nav_Logs", Logs),
            new NavigationItem("Nav_Settings", Settings),
        ];
        _selected = NavigationItems[0];

        NavigateCommand = ReactiveCommand.Create<string>(key =>
        {
            foreach (var n in NavigationItems)
            {
                if (n.TitleKey == key)
                {
                    Selected = n;
                    return;
                }
            }
        });

        GoAdvancedCommand = ReactiveCommand.Create(() =>
        {
            foreach (var n in NavigationItems)
            {
                if (n.Content == Advanced)
                {
                    Selected = n;
                    return;
                }
            }
        });
    }

    public ObservableCollection<NavigationItem> NavigationItems { get; }

    public NavigationItem Selected
    {
        get => _selected;
        set => this.RaiseAndSetIfChanged(ref _selected, value);
    }

    public ReactiveCommand<string, Unit> NavigateCommand { get; }
    public ReactiveCommand<Unit, Unit> GoAdvancedCommand { get; }

    public OverviewViewModel Overview { get; }
    public ConnectionViewModel Connection { get; }
    public PartitionsViewModel Partitions { get; }
    public SectorsViewModel Sectors { get; }
    public RawProgramViewModel RawProgram { get; }
    public AdvancedViewModel Advanced { get; }
    public LogsViewModel Logs { get; }
    public SettingsViewModel Settings { get; }
}

public sealed class NavigationItem : ViewModelBase
{
    public NavigationItem(string titleKey, ViewModelBase content)
    {
        TitleKey = titleKey;
        Content = content;
        Localizer.Instance.CultureChanged += (_, _) => this.RaisePropertyChanged(nameof(Title));
    }

    public string TitleKey { get; }
    public ViewModelBase Content { get; }
    public string Title => Localizer.Instance[TitleKey];
}

public sealed class PlaceholderViewModel : ViewModelBase
{
    public PlaceholderViewModel(string titleKey, string descriptionKey)
    {
        TitleKey = titleKey;
        DescriptionKey = descriptionKey;
        Localizer.Instance.CultureChanged += (_, _) =>
        {
            this.RaisePropertyChanged(nameof(Title));
            this.RaisePropertyChanged(nameof(Description));
        };
    }

    public string TitleKey { get; }
    public string DescriptionKey { get; }
    public string Title => Localizer.Instance[TitleKey];
    public string Description => Localizer.Instance[DescriptionKey];
}