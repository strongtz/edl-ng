using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using QCEDL.GUI.Services;
using QCEDL.GUI.ViewModels;

namespace QCEDL.GUI.Views;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnLinkClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string url || string.IsNullOrWhiteSpace(url))
        {
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception)
        {
            // Best-effort; swallow if the shell can't open the URL.
        }
    }

    private async void OnViewLicenseClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string key || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var title = key == "edl-ng"
            ? Localizer.Instance["Menu_About_Title"]
            : key;
        var body = LicenseTexts.Load(key);
        var dialog = new LicenseViewerDialog
        {
            DataContext = new LicenseViewerViewModel(title, body),
        };
        await dialog.ShowDialog(this);
    }

    public static async Task ShowAsync(Window owner)
    {
        var dialog = new AboutDialog { DataContext = new AboutDialogViewModel() };
        await dialog.ShowDialog(owner);
    }
}