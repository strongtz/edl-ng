using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Qualcomm.EmergencyDownload.Helpers;

namespace QCEDL.GUI.Services;

/// <summary>
/// Persists GUI preferences (culture, log level, last-used Connection options) as JSON under
/// the user's AppData directory. Failures are logged but non-fatal — the GUI falls back to
/// defaults. A single <see cref="Current"/> instance is shared across the app so partial
/// updates (e.g. only the language picker) don't clobber other persisted fields.
/// </summary>
public static class GuiSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static GuiSettingsModel? s_current;

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "edl-ng",
        "gui-settings.json");

    public static GuiSettingsModel Current
    {
        get
        {
            s_current ??= Load();
            return s_current;
        }
    }

    public static GuiSettingsModel Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                s_current = new GuiSettingsModel();
                return s_current;
            }

            var json = File.ReadAllText(FilePath);
            s_current = JsonSerializer.Deserialize<GuiSettingsModel>(json) ?? new GuiSettingsModel();
            return s_current;
        }
        catch (Exception ex)
        {
            Logging.Log($"Failed to load GUI settings: {ex.Message}", LogLevel.Warning);
            s_current = new GuiSettingsModel();
            return s_current;
        }
    }

    /// <summary>Persist the shared <see cref="Current"/> instance.</summary>
    public static void Save() => Save(Current);

    public static void Save(GuiSettingsModel model)
    {
        s_current = model;
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var json = JsonSerializer.Serialize(model, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            Logging.Log($"Failed to save GUI settings: {ex.Message}", LogLevel.Warning);
        }
    }

    public static CultureInfo ResolveStartupCulture(GuiSettingsModel model)
    {
        if (!string.IsNullOrWhiteSpace(model.Culture))
        {
            try
            {
                var requested = CultureInfo.GetCultureInfo(model.Culture);
                if (Localizer.SupportedCultures.Any(c => c.Name == requested.Name))
                {
                    return requested;
                }
            }
            catch (CultureNotFoundException)
            {
                // Fall through to detection below.
            }
        }

        var system = CultureInfo.CurrentUICulture;
        var match = Localizer.SupportedCultures.FirstOrDefault(c => c.Name == system.Name)
                    ?? Localizer.SupportedCultures.FirstOrDefault(c => c.TwoLetterISOLanguageName == system.TwoLetterISOLanguageName);
        return match ?? Localizer.SupportedCultures[0];
    }

    public static LogLevel ResolveStartupLogLevel(GuiSettingsModel model)
    {
        return !string.IsNullOrWhiteSpace(model.LogLevel)
            && Enum.TryParse<LogLevel>(model.LogLevel, ignoreCase: true, out var level)
            ? level
            : LogLevel.Info;
    }

    public static AppTheme ResolveStartupTheme(GuiSettingsModel model)
    {
        return !string.IsNullOrWhiteSpace(model.Theme)
            && Enum.TryParse<AppTheme>(model.Theme, ignoreCase: true, out var theme)
            ? theme
            : AppTheme.System;
    }
}

public sealed class GuiSettingsModel
{
    [JsonPropertyName("culture")]
    public string? Culture { get; set; }

    [JsonPropertyName("logLevel")]
    public string? LogLevel { get; set; }

    [JsonPropertyName("theme")]
    public string? Theme { get; set; }

    [JsonPropertyName("loaderPath")]
    public string? LoaderPath { get; set; }

    [JsonPropertyName("vidHex")]
    public string? VidHex { get; set; }

    [JsonPropertyName("pidHex")]
    public string? PidHex { get; set; }

    [JsonPropertyName("memoryType")]
    public string? MemoryType { get; set; }

    [JsonPropertyName("backend")]
    public string? Backend { get; set; }
}