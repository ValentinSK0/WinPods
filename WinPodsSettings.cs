using System.Text.Json;

namespace WinPods;

public sealed class WinPodsSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public bool CallQualityGuardEnabled { get; set; } = true;

    public bool CallQualityNotificationsEnabled { get; set; } = true;

    public int? WindowLeft { get; set; }

    public int? WindowTop { get; set; }

    public int? WindowWidth { get; set; }

    public int? WindowHeight { get; set; }

    public bool WindowMaximized { get; set; }

    public int? MainSplitterDistance { get; set; }

    public string? DeviceSortColumn { get; set; }

    public string? DeviceSortOrder { get; set; }

    public static WinPodsSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path))
            {
                return new WinPodsSettings();
            }

            return JsonSerializer.Deserialize<WinPodsSettings>(File.ReadAllText(path)) ?? new WinPodsSettings();
        }
        catch
        {
            return new WinPodsSettings();
        }
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinPods",
            "settings.json");
}
