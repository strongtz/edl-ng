using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace QCEDL.GUI.Views;

public partial class OverviewView : UserControl
{
    public OverviewView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}