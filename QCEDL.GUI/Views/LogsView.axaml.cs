using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace QCEDL.GUI.Views;

public partial class LogsView : UserControl
{
    public LogsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}