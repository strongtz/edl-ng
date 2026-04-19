using QCEDL.GUI.Services;
using ReactiveUI;

namespace QCEDL.GUI.ViewModels;

public sealed class ConfirmDialogViewModel : ViewModelBase
{
    private string _typedConfirmation = string.Empty;

    public ConfirmDialogViewModel(
        string title,
        string message,
        string confirmLabel,
        string cancelLabel,
        bool danger,
        string? requiredConfirmation,
        string? linkText = null,
        string? linkUrl = null,
        bool showCancel = true)
    {
        Title = title;
        Message = message;
        ConfirmLabel = confirmLabel;
        CancelLabel = cancelLabel;
        Danger = danger;
        RequiredConfirmation = requiredConfirmation;
        RequiresTyping = !string.IsNullOrEmpty(requiredConfirmation);
        TypeToConfirmPrompt = RequiresTyping
            ? Localizer.Instance.Format("Confirm_TypeToConfirmFormat", requiredConfirmation)
            : string.Empty;
        LinkText = linkText;
        LinkUrl = linkUrl;
        HasLink = !string.IsNullOrWhiteSpace(linkText) && !string.IsNullOrWhiteSpace(linkUrl);
        ShowCancel = showCancel;
    }

    public string Title { get; }
    public string Message { get; }
    public string ConfirmLabel { get; }
    public string CancelLabel { get; }
    public bool Danger { get; }
    public string? RequiredConfirmation { get; }
    public bool RequiresTyping { get; }
    public string TypeToConfirmPrompt { get; }
    public string? LinkText { get; }
    public string? LinkUrl { get; }
    public bool HasLink { get; }
    public bool ShowCancel { get; }

    public string TypedConfirmation
    {
        get => _typedConfirmation;
        set
        {
            this.RaiseAndSetIfChanged(ref _typedConfirmation, value);
            this.RaisePropertyChanged(nameof(CanConfirm));
        }
    }

    public bool CanConfirm => !RequiresTyping ||
        string.Equals(_typedConfirmation, RequiredConfirmation, StringComparison.Ordinal);
}