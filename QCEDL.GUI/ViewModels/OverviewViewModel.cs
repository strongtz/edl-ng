using QCEDL.GUI.Services;
using ReactiveUI;

namespace QCEDL.GUI.ViewModels;

public sealed class OverviewViewModel : ViewModelBase
{
    private readonly EdlService _service;

    public OverviewViewModel(EdlService service, ConnectionViewModel connection)
    {
        _service = service;
        Connection = connection;

        // React to mode/status changes in the connection VM so the overview stays live.
        connection.WhenAnyValue(x => x.ModeText).Subscribe(_ => this.RaisePropertyChanged(nameof(ConnectionSummary)));
        connection.WhenAnyValue(x => x.StatusText).Subscribe(_ => this.RaisePropertyChanged(nameof(ConnectionSummary)));

        Localizer.Instance.CultureChanged += (_, _) =>
        {
            this.RaisePropertyChanged(nameof(TagLine));
            this.RaisePropertyChanged(nameof(ConnectionSummary));
        };
    }

    public ConnectionViewModel Connection { get; }

    public string ConnectionSummary =>
        _service.IsConnected
            ? Localizer.Instance.Format("Overview_ConnectedFormat", _service.CurrentMode)
            : Connection.StatusText;

    public string TagLine => Localizer.Instance["Overview_TagLine"];
}