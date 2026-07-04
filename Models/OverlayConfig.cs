using System.IO;
using System.Text.Json;

namespace TinyFpsOverlay.Models;

public sealed class OverlayConfig
{
    public double Left { get; set; } = -1;
    public double Top { get; set; } = 0;
    public bool LockedClickThrough { get; set; } = true;
    public double Opacity { get; set; } = 1.0;
    public bool HotkeyEnabled { get; set; } = true;
    public bool AutoStartEnabled { get; set; } = false;
    public int TextColorArgb { get; set; } = unchecked((int)0xFFAAFF00); // 小飞机风格荧光绿
}

public static class OverlayConfigStore
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TinyFpsOverlay");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    public static OverlayConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return new OverlayConfig();
            }

            string json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<OverlayConfig>(json) ?? new OverlayConfig();
        }
        catch
        {
            return new OverlayConfig();
        }
    }

    public static void Save(OverlayConfig config)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // ignored
        }
    }
}




