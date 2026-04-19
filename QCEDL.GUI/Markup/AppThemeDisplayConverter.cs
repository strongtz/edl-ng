using System.Globalization;
using Avalonia.Data.Converters;
using QCEDL.GUI.Services;

namespace QCEDL.GUI.Markup;

/// <summary>
/// Resolves an <see cref="AppTheme"/> enum value to its localized display name
/// via <see cref="Localizer"/>. Used in the Settings theme picker's ComboBox
/// item template so list items reflect the active language.
/// </summary>
public sealed class AppThemeDisplayConverter : IValueConverter
{
    public static readonly AppThemeDisplayConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is AppTheme theme)
        {
            var key = theme switch
            {
                AppTheme.Light => "Settings_Theme_Light",
                AppTheme.Dark => "Settings_Theme_Dark",
                AppTheme.System => "Settings_Theme_System",
                _ => "Settings_Theme_System",
            };
            return Localizer.Instance[key];
        }
        return value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}