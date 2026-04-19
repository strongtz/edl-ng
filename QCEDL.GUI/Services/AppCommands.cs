using System.Diagnostics;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using QCEDL.GUI.Views;
using ReactiveUI;

namespace QCEDL.GUI.Services;

/// <summary>
/// Static commands bound from NativeMenu items that don't need the shell's DataContext
/// (so they can live in App.axaml and in MainWindow.axaml without wiring a view-model path).
/// </summary>
public static class AppCommands
{
    public static ICommand OpenDocs { get; } = ReactiveCommand.Create(() =>
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/strongtz/edl-ng",
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            // Best-effort; swallow if the shell can't open the URL.
        }
    });

    public static ICommand ShowAbout { get; } = ReactiveCommand.CreateFromTask(async () =>
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime d
            || d.MainWindow is null)
        {
            return;
        }

        await AboutDialog.ShowAsync(d.MainWindow);
    });
}