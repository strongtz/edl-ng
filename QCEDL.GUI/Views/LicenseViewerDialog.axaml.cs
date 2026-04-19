using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace QCEDL.GUI.Views;

public partial class LicenseViewerDialog : Window
{
    public LicenseViewerDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnClose(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}