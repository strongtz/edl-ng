using System.Globalization;
using QCEDL.GUI.Services;
using Qualcomm.EmergencyDownload.Helpers;
using ReactiveUI;

namespace QCEDL.GUI.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private CultureInfo _selectedLanguage;
    private LogLevel _selectedLogLevel;
    private AppTheme _selectedTheme;

    public SettingsViewModel()
    {
        _selectedLanguage = Localizer.Instance.Culture;
        _selectedLogLevel = Logging.CurrentLogLevel;
        _selectedTheme = ThemeService.Instance.Current;

        // Keep selection synced if culture is changed elsewhere (e.g. from startup defaults).
        Localizer.Instance.CultureChanged += (_, _) =>
        {
            if (!Equals(_selectedLanguage, Localizer.Instance.Culture))
            {
                _selectedLanguage = Localizer.Instance.Culture;
                this.RaisePropertyChanged(nameof(SelectedLanguage));
            }
        };

        ThemeService.Instance.ThemeChanged += (_, _) =>
        {
            if (_selectedTheme != ThemeService.Instance.Current)
            {
                _selectedTheme = ThemeService.Instance.Current;
                this.RaisePropertyChanged(nameof(SelectedTheme));
            }
        };
    }

    public IReadOnlyList<CultureInfo> Languages { get; } = Localizer.SupportedCultures;
    public IReadOnlyList<LogLevel> LogLevels { get; } = Enum.GetValues<LogLevel>();
    public IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();

    public CultureInfo SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value is null || Equals(_selectedLanguage, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedLanguage, value);
            Localizer.Instance.Culture = value;
            GuiSettings.Current.Culture = value.Name;
            GuiSettings.Save();
        }
    }

    public LogLevel SelectedLogLevel
    {
        get => _selectedLogLevel;
        set
        {
            if (_selectedLogLevel == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedLogLevel, value);
            Logging.CurrentLogLevel = value;
            GuiSettings.Current.LogLevel = value.ToString();
            GuiSettings.Save();
        }
    }

    public AppTheme SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (_selectedTheme == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedTheme, value);
            ThemeService.Instance.Apply(value);
            GuiSettings.Current.Theme = value.ToString();
            GuiSettings.Save();
        }
    }

    public string ThemeDisplayName(AppTheme theme) => theme switch
    {
        AppTheme.Light => Localizer.Instance["Settings_Theme_Light"],
        AppTheme.Dark => Localizer.Instance["Settings_Theme_Dark"],
        AppTheme.System => Localizer.Instance["Settings_Theme_System"],
        _ => Localizer.Instance["Settings_Theme_System"],
    };
}