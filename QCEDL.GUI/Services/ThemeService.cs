using System.ComponentModel;
using Avalonia;
using Avalonia.Styling;

namespace QCEDL.GUI.Services;

/// <summary>
/// Singleton that owns the app-wide <see cref="ThemeVariant"/>. Mirrors
/// <see cref="Localizer"/>'s shape: settable <see cref="Current"/>, a
/// <see cref="ThemeChanged"/> event, and <see cref="INotifyPropertyChanged"/>
/// so XAML bindings and VMs can react.
///
/// The actual palette swap is driven by Avalonia's native
/// <c>ResourceDictionary.ThemeDictionaries</c> in <c>Themes/Tokens.axaml</c>:
/// setting <see cref="Application.RequestedThemeVariant"/> triggers every
/// <c>DynamicResource</c> brush to re-resolve.
/// </summary>
public sealed class ThemeService : INotifyPropertyChanged
{
    private static readonly Lazy<ThemeService> Singleton = new(() => new ThemeService());

    public static ThemeService Instance => Singleton.Value;

    public AppTheme Current { get; private set; } = AppTheme.System;

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? ThemeChanged;

    public void Apply(AppTheme theme)
    {
        Current = theme;

        var app = Application.Current;
        if (app is not null)
        {
            app.RequestedThemeVariant = theme switch
            {
                AppTheme.Light => ThemeVariant.Light,
                AppTheme.Dark => ThemeVariant.Dark,
                AppTheme.System => ThemeVariant.Default,
                _ => ThemeVariant.Default,
            };
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }
}

public enum AppTheme
{
    System,
    Light,
    Dark,
}