using Avalonia.Data;
using Avalonia.Markup.Xaml;
using QCEDL.GUI.Services;

namespace QCEDL.GUI.Markup;

/// <summary>
/// XAML markup extension resolving a resx key to a live binding against <see cref="Localizer.Instance"/>.
/// Usage: <c>Text="{l:Localize Nav_Overview}"</c>. Bindings re-evaluate whenever the active culture
/// changes because <see cref="Localizer"/> raises <c>PropertyChanged("Item[]")</c>.
/// </summary>
public sealed class LocalizeExtension : MarkupExtension
{
    public LocalizeExtension()
    {
        Key = string.Empty;
    }

    public LocalizeExtension(string key)
    {
        Key = key;
    }

    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]")
        {
            Source = Localizer.Instance,
            Mode = BindingMode.OneWay,
        };
}