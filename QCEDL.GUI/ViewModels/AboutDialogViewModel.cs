namespace QCEDL.GUI.ViewModels;

public sealed class AboutDialogViewModel : ViewModelBase
{
    public AboutDialogViewModel()
    {
        Dependencies =
        [
            new DependencyInfo("Avalonia", "MIT", "avalonia", "https://github.com/AvaloniaUI/Avalonia"),
            new DependencyInfo("ReactiveUI", "MIT", "reactiveui", "https://github.com/reactiveui/ReactiveUI"),
            new DependencyInfo("Inter font family", "SIL OFL 1.1", "inter", "https://github.com/rsms/inter"),
            new DependencyInfo("System.IO.Ports", "MIT", "dotnet-runtime", "https://github.com/dotnet/runtime"),
            new DependencyInfo("LibUsbDotNet", "LGPL-3.0", "libusbdotnet", "https://github.com/LibUsbDotNet/LibUsbDotNet"),
            new DependencyInfo("Vanara.PInvoke", "MIT", "vanara", "https://github.com/dahall/Vanara"),
            new DependencyInfo("QCEDL.NET (by gus33000)", "MIT", "qcedl-net", "https://github.com/gus33000/QCEDL.NET"),
        ];
    }

    public IReadOnlyList<DependencyInfo> Dependencies { get; }
}

public sealed class DependencyInfo
{
    public DependencyInfo(string name, string license, string licenseKey, string url)
    {
        Name = name;
        License = license;
        LicenseKey = licenseKey;
        Url = url;
    }

    public string Name { get; }
    public string License { get; }
    public string LicenseKey { get; }
    public string Url { get; }
}