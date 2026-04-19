namespace QCEDL.GUI.ViewModels;

public sealed class LicenseViewerViewModel : ViewModelBase
{
    public LicenseViewerViewModel(string title, string body)
    {
        Title = title;
        Body = body;
    }

    public string Title { get; }
    public string Body { get; }
}