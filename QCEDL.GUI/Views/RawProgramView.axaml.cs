using System.Reactive.Disposables;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using QCEDL.GUI.Services;
using QCEDL.GUI.ViewModels;

namespace QCEDL.GUI.Views;

public partial class RawProgramView : UserControl
{
    private readonly CompositeDisposable _subs = [];

    public RawProgramView()
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
        if (DataContext is RawProgramViewModel vm)
        {
            _subs.Add(DialogBridges.RegisterPickFile(this, vm.PickFile));
            _subs.Add(DialogBridges.RegisterPickFolder(this, vm.PickFolder));
            _subs.Add(DialogBridges.RegisterConfirm(this, vm.Confirm));
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _subs.Clear();
        base.OnDetachedFromVisualTree(e);
    }
}