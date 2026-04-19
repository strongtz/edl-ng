using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace QCEDL.GUI.Views;

public partial class PlaceholderView : UserControl
{
    public PlaceholderView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}