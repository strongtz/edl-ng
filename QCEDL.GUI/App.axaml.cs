using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QCEDL.GUI.Helper;
using QCEDL.GUI.ViewModels;
using QCEDL.GUI.Views;

namespace QCEDL.GUI;

public partial class App : Application
{
    private IHost _host = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((_, services) =>
                {
                    if (OperatingSystem.IsMacOS())
                    {
                        AddMacOSServices(services);
                    }
                })
                .Build();

            _host.Start();

            desktop.MainWindow = new MainWindow { DataContext = new MainWindowViewModel() };
            desktop.Exit += (_, _) =>
            {
                _host.StopAsync()
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
                _host.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    [SupportedOSPlatform("macOS")]
    private static void AddMacOSServices(IServiceCollection services)
    {
        services
            .AddSingleton<MacOSHelperLauncher>()
            .AddHostedService(sp => sp.GetRequiredService<MacOSHelperLauncher>())
            .AddSingleton<IHelperLauncher>(sp => sp.GetRequiredService<MacOSHelperLauncher>());
    }
}