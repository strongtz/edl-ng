using QCEDL.GUI.Services;

namespace QCEDL.GUI.ViewModels;

public sealed class LogsViewModel : ViewModelBase
{
    public LogsViewModel(ObservableLogSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        Sink = sink;
    }

    public ObservableLogSink Sink { get; }

    public void Clear()
    {
        Sink.Clear();
    }
}