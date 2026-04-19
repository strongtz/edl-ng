using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using QCEDL.GUI.Services;
using QCEDL.GUI.ViewModels;
using QCEDL.GUI.Views;
using QCEDL.NET.Logging;
using Qualcomm.EmergencyDownload.Helpers;

namespace QCEDL.GUI;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Apply persisted or OS-derived UI culture before any view is constructed so
            // initial bindings already see the right language.
            var settings = GuiSettings.Load();
            Localizer.Instance.Culture = GuiSettings.ResolveStartupCulture(settings);
            Logging.CurrentLogLevel = GuiSettings.ResolveStartupLogLevel(settings);

            // Apply the persisted (or default) theme before any view is constructed so
            // first paint already matches the user's preference — avoids a light-to-dark
            // flicker on startup.
            ThemeService.Instance.Apply(GuiSettings.ResolveStartupTheme(settings));

            // Swap the FontSerif/Sans/Mono resources to match the active script, and keep
            // them in sync when the user picks a different language from Settings.
            FontTheme.Apply(Localizer.Instance.Culture);
            Localizer.Instance.CultureChanged += (_, _) => FontTheme.Apply(Localizer.Instance.Culture);

            // Route all CLI Logging output into the live log sink the GUI binds to, and
            // silence the console sink by default (the GUI owns the surface now).
            Logging.ConsoleSinkEnabled = false;
            var logSink = new ObservableLogSink();
            Logging.LogEmitted += logSink.Emit;

            // Protocol-layer logs (Sahara/Firehose) flow through LibraryLogger; forward
            // them into the same pipeline so the Logs view surfaces errors like
            // "Transfer crash" / "Unexpected Sahara command" that would otherwise be dropped.
            LibraryLogger.LogAction = (message, level, _, _, _) => Logging.Log(message, (Qualcomm.EmergencyDownload.Helpers.LogLevel)level);

            var edlService = new EdlService();
            var shell = new ShellViewModel(edlService, logSink);

            desktop.MainWindow = new MainWindow
            {
                DataContext = shell,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}