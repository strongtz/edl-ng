using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace QCEDL.GUI.Services;

/// <summary>
/// Singleton that wraps <see cref="ResourceManager"/> so XAML bindings and ViewModels can read
/// localized strings by key and react to runtime culture changes.
/// </summary>
public sealed class Localizer : INotifyPropertyChanged
{
    private static readonly Lazy<Localizer> Singleton = new(() => new Localizer());
    private readonly ResourceManager _rm = new("QCEDL.GUI.Resource.Strings", typeof(Localizer).Assembly);
    private CultureInfo _culture = CultureInfo.CurrentUICulture;

    public static Localizer Instance => Singleton.Value;

    public static IReadOnlyList<CultureInfo> SupportedCultures { get; } =
    [
        CultureInfo.GetCultureInfo("en"),
        CultureInfo.GetCultureInfo("zh-Hans"),
        CultureInfo.GetCultureInfo("zh-Hant"),
        CultureInfo.GetCultureInfo("ja"),
    ];

    public CultureInfo Culture
    {
        get => _culture;
        set
        {
            if (value is null || Equals(_culture, value))
            {
                return;
            }

            _culture = value;
            CultureInfo.CurrentUICulture = value;
            CultureInfo.DefaultThreadCurrentUICulture = value;
            Thread.CurrentThread.CurrentUICulture = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Culture)));
            CultureChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string this[string key] => _rm.GetString(key, _culture) ?? $"!{key}!";

    public string Format(string key, params object?[] args) =>
        string.Format(_culture, this[key], args);

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? CultureChanged;
}