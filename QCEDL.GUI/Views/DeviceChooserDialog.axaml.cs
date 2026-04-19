using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using QCEDL.GUI.Services;
using QCEDL.GUI.ViewModels;
using Qualcomm.EmergencyDownload.Core;

namespace QCEDL.GUI.Views;

public partial class DeviceChooserDialog : Window
{
    public DeviceChooserDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DeviceChooserDialogViewModel vm && vm.CanConfirm)
        {
            Close(vm.SelectedCandidate);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    public static async Task<DeviceCandidate?> ShowAsync(Window owner, IReadOnlyList<DeviceCandidate> candidates)
    {
        var vm = new DeviceChooserDialogViewModel(
            candidates,
            Localizer.Instance["DeviceChooser_Title"],
            Localizer.Instance["DeviceChooser_Prompt"],
            Localizer.Instance["DeviceChooser_Confirm"],
            Localizer.Instance["DeviceChooser_Cancel"]);

        var dialog = new DeviceChooserDialog { DataContext = vm };
        return await dialog.ShowDialog<DeviceCandidate?>(owner);
    }
}