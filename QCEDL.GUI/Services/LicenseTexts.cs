using Avalonia.Platform;

namespace QCEDL.GUI.Services;

/// <summary>
/// Loads the embedded license text files shipped as AvaloniaResource under
/// <c>avares://qcedl-gui/Assets/Licenses/</c>. Keys are SPDX-ish identifiers
/// (MIT, LGPL-2.1, OFL-1.1) plus <c>edl-ng</c> for the project's own LICENSE.
/// </summary>
public static class LicenseTexts
{
    public static string Load(string key)
    {
        var uri = new Uri($"avares://qcedl-gui/Assets/Licenses/{key}.txt");
        try
        {
            using var stream = AssetLoader.Open(uri);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception)
        {
            return Localizer.Instance.Format("License_NotFoundFormat", key);
        }
    }
}