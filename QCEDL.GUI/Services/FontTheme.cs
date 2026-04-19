using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace QCEDL.GUI.Services;

/// <summary>
/// Swaps the <c>FontSerif</c> / <c>FontSans</c> / <c>FontMono</c> resources on the running
/// <see cref="Application"/> so that CJK locales actually render headlines in a CJK *serif*
/// face. Avalonia's per-glyph fallback defaults to the OS sans CJK font regardless of how
/// serifs are ordered inside a single FontFamily chain, so we have to pick the primary
/// family up-front based on the active culture.
/// </summary>
public static class FontTheme
{
    // Latin primaries, kept in sync with DESIGN.md §3.
    private const string LatinSerif = "Georgia, Times New Roman, serif";
    private const string LatinSans = "Inter, Helvetica Neue, Arial, sans-serif";
    private const string LatinMono = "JetBrains Mono, Menlo, Consolas, monospace";

    // CJK serif families (macOS, Windows, Linux) — covers Ming/Song-style faces.
    private const string SerifZhHans = "Songti SC, STSong, Source Han Serif SC, Noto Serif CJK SC, Noto Serif SC, SimSun";
    private const string SerifZhHant = "Songti TC, STSong, LiSong Pro, Source Han Serif TC, Noto Serif CJK TC, Noto Serif TC, PMingLiU, MingLiU";
    private const string SerifJa = "Hiragino Mincho ProN, YuMincho, Yu Mincho, Source Han Serif JP, Noto Serif CJK JP, Noto Serif JP, MS Mincho";

    // CJK sans families — Gothic / Hei faces.
    private const string SansZhHans = "PingFang SC, Source Han Sans SC, Noto Sans CJK SC, Noto Sans SC, Microsoft YaHei UI, Microsoft YaHei";
    private const string SansZhHant = "PingFang TC, Source Han Sans TC, Noto Sans CJK TC, Noto Sans TC, Microsoft JhengHei UI, Microsoft JhengHei";
    private const string SansJa = "Hiragino Sans, Hiragino Kaku Gothic ProN, Yu Gothic UI, Yu Gothic, Meiryo, Source Han Sans JP, Noto Sans CJK JP, Noto Sans JP";

    public static void Apply(CultureInfo culture)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var (serif, sans, mono) = Resolve(culture);
        app.Resources["FontSerif"] = new FontFamily(serif);
        app.Resources["FontSans"] = new FontFamily(sans);
        app.Resources["FontMono"] = new FontFamily(mono);
    }

    private static (string Serif, string Sans, string Mono) Resolve(CultureInfo culture)
    {
        // Match on the Chinese script (Hans/Hant) or the full zh-CN / zh-TW pair, then language.
        var name = culture.Name;
        var lang = culture.TwoLetterISOLanguageName;

        if (name.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase)
            || name.Equals("zh-TW", StringComparison.OrdinalIgnoreCase)
            || name.Equals("zh-HK", StringComparison.OrdinalIgnoreCase)
            || name.Equals("zh-MO", StringComparison.OrdinalIgnoreCase))
        {
            return ($"{SerifZhHant}, {LatinSerif}",
                    $"{LatinSans}, {SansZhHant}",
                    $"{LatinMono}, {SansZhHant}, monospace");
        }

        if (lang.Equals("zh", StringComparison.OrdinalIgnoreCase))
        {
            return ($"{SerifZhHans}, {LatinSerif}",
                    $"{LatinSans}, {SansZhHans}",
                    $"{LatinMono}, {SansZhHans}, monospace");
        }

        if (lang.Equals("ja", StringComparison.OrdinalIgnoreCase))
        {
            return ($"{SerifJa}, {LatinSerif}",
                    $"{LatinSans}, {SansJa}",
                    $"{LatinMono}, {SansJa}, monospace");
        }

        return (LatinSerif, LatinSans, LatinMono);
    }
}