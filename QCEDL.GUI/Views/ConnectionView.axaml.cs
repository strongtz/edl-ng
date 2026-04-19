using System.Reactive.Disposables;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using QCEDL.GUI.Services;
using QCEDL.GUI.ViewModels;

namespace QCEDL.GUI.Views;

public partial class ConnectionView : UserControl
{
    private readonly CompositeDisposable _subs = [];

    public ConnectionView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is ConnectionViewModel vm)
        {
            _subs.Add(DialogBridges.RegisterPickFile(this, vm.PickFile));
            _subs.Add(DialogBridges.RegisterPickDevice(this, vm.PickDevice));
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _subs.Clear();
        base.OnDetachedFromVisualTree(e);
    }
}