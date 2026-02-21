using System.Text.Json;
using System.Text.Json.Serialization;

namespace HoverPulse.Services;

/// <summary>
/// Application settings, persisted to %APPDATA%\HoverPulse\settings.json.
/// </summary>
internal sealed class AppSettings
{
    public int DwellTimeMs { get; set; } = 1000;
    public int ToleranceRadius { get; set; } = 10;
    public bool ClickLock { get; set; }
    public bool AudioEnabled { get; set; } = true;
    public bool ShowIndicator { get; set; } = true;
    public int IndicatorRadius { get; set; } = 24;
    public string IndicatorColor { get; set; } = "#3399FF";
    public float ToolbarOpacity { get; set; } = 0.9f;
    public int ToolbarX { get; set; } = 100;
    public int ToolbarY { get; set; } = 100;
    public bool ToolbarHorizontal { get; set; } = true;
    public bool AutoStart { get; set; }

    // Hotkeys (virtual key codes)
    public uint ToggleHotkey { get; set; } = 0x78;  // F9
    public uint CycleClickTypeHotkey { get; set; } = 0x79;  // F10
    public uint PauseHotkey { get; set; } = 0x7A;  // F11
}

/// <summary>
/// Loads and saves application settings to a JSON file.
/// </summary>
internal static class SettingsService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HoverPulse");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
    };

    /// <summary>Loads settings from disk, or returns defaults if file doesn't exist.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            // If settings file is corrupt, return defaults
        }

        return new AppSettings();
    }

    /// <summary>Saves settings to disk.</summary>
    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Settings save failure is non-fatal
        }
    }
}
