using Avalonia;
using ReactiveUI.Avalonia;

namespace QCEDL.GUI;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        return BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI(_ => { });
    }
}