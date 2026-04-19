using System.Collections.ObjectModel;
using Qualcomm.EmergencyDownload.Core;
using ReactiveUI;

namespace QCEDL.GUI.ViewModels;

public sealed class DeviceChooserDialogViewModel : ViewModelBase
{
    private DeviceCandidate? _selectedCandidate;

    public DeviceChooserDialogViewModel(IReadOnlyList<DeviceCandidate> candidates, string title, string prompt, string confirmLabel, string cancelLabel)
    {
        Candidates = new ObservableCollection<DeviceCandidate>(candidates);
        if (Candidates.Count > 0)
        {
            _selectedCandidate = Candidates[0];
        }
        Title = title;
        Prompt = prompt;
        ConfirmLabel = confirmLabel;
        CancelLabel = cancelLabel;
    }

    public ObservableCollection<DeviceCandidate> Candidates { get; }
    public string Title { get; }
    public string Prompt { get; }
    public string ConfirmLabel { get; }
    public string CancelLabel { get; }

    public DeviceCandidate? SelectedCandidate
    {
        get => _selectedCandidate;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedCandidate, value);
            this.RaisePropertyChanged(nameof(CanConfirm));
        }
    }

    public bool CanConfirm => _selectedCandidate is not null;
}