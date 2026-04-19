using System.Reactive.Disposables;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using QCEDL.GUI.Services;
using QCEDL.GUI.ViewModels;

namespace QCEDL.GUI.Views;

public partial class SectorsView : UserControl
{
    private readonly CompositeDisposable _subs = [];

    public SectorsView()
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
        if (DataContext is SectorsViewModel vm)
        {
            _subs.Add(DialogBridges.RegisterSaveFile(this, vm.PickSaveFile));
            _subs.Add(DialogBridges.RegisterPickFile(this, vm.PickOpenFile));
            _subs.Add(DialogBridges.RegisterConfirm(this, vm.Confirm));
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _subs.Clear();
        base.OnDetachedFromVisualTree(e);
    }
}