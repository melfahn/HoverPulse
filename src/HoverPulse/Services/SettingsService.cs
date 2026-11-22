using System.Text.Json;
using System.Text.Json.Serialization;
using HoverPulse.Core;

namespace HoverPulse.Services;

internal sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 2;
    public int ClickTime { get; set; } = 2;
    public int MoveLimit { get; set; } = 5;
    public ClickType DefaultAction { get; set; }
    public bool AlternateInput { get; set; }
    public bool AudioEnabled { get; set; }
    public int AppSize { get; set; } = 1;
    public int FloatMode { get; set; } = 2;
    public bool ShowIndicator { get; set; }
    public string IndicatorColor { get; set; } = "#3399FF";
    public float ToolbarOpacity { get; set; } = 0.9f;
    public int ToolbarX { get; set; } = 980;
    public int ToolbarY { get; set; } = 24;
    public bool ToolbarHorizontal { get; set; } = true;
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalSettings { get; set; }

    public AppSettings Copy() => (AppSettings)MemberwiseClone();

    internal void Validate()
    {
        ClickTime = Math.Clamp(ClickTime, 1, 50);
        MoveLimit = Math.Clamp(MoveLimit, 2, 20);
        AppSize = Math.Clamp(AppSize, 1, 3);
        FloatMode = Math.Clamp(FloatMode, 0, 2);
        try { _ = System.Drawing.ColorTranslator.FromHtml(IndicatorColor); }
        catch (Exception error) when (error is ArgumentException or FormatException) { IndicatorColor = "#3399FF"; }
        ToolbarOpacity = Math.Clamp(ToolbarOpacity, 0.3f, 1);
        if (!Enum.IsDefined(DefaultAction) || DefaultAction == ClickType.DragEnd)
        {
            DefaultAction = ClickType.LeftClick;
        }
    }
}

internal sealed class SettingsService(string path)
{
    public static SettingsService ForUser() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HoverPulse", "settings.json"));

    public AppSettings Load()
    {
        foreach (var file in new[] { path, path + ".bak" })
        {
            if (!File.Exists(file))
            {
                continue;
            }

            try
            {
                var json = File.ReadAllText(file);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                bool legacy = !doc.RootElement.TryGetProperty("SchemaVersion", out var version);
                if (!legacy && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out _)))
                {
                    continue;
                }

                if (!legacy && version.GetInt32() != 2)
                {
                    throw new NotSupportedException("Unsupported HoverPulse settings version.");
                }

                var settings = JsonSerializer.Deserialize<AppSettings>(json)!;
                if (legacy)
                {
                    if (!File.Exists(path + ".v1"))
                    {
                        File.Copy(file, path + ".v1");
                    }
                    // Keep the old elapsed-time and radius values as extension data.
                    settings.AudioEnabled = !doc.RootElement.TryGetProperty("AudioEnabled", out var sound) || sound.GetBoolean();
                    settings.ShowIndicator = !doc.RootElement.TryGetProperty("ShowIndicator", out var ring) || ring.GetBoolean();
                }
                settings.Validate();
                return settings;
            }
            catch (JsonException) { }
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings);
                stream.Flush(true);
            }
            if (File.Exists(path))
            {
                // Only rotate a readable current file into the last-good slot.
                bool readable;
                try { readable = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) != null; }
                catch (JsonException) { readable = false; }
                File.Replace(temp, path, readable ? path + ".bak" : null);
            }
            else
            {
                File.Move(temp, path);
            }
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }
}
