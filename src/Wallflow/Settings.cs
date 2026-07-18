using System.IO;
using System.Text.Json;

namespace Wallflow;

public class Settings
{
    public string? LastWallpaper { get; set; }
    public List<string> Recents { get; set; } = [];
    public bool AutoStart { get; set; } = true;
    public bool AutoPauseEnabled { get; set; } = true;

    private int _volume = 100;
    public int Volume
    {
        get => _volume;
        set => _volume = value switch { < 0 => 0, > 100 => 100, _ => value };
    }

    public bool Muted { get; set; }

    private string _videoFit = "cover";
    public string VideoFit
    {
        get => _videoFit;
        set => _videoFit = value is "cover" or "fit" or "fill" ? value : "cover";
    }

    public bool Loop { get; set; } = true;

    private double _speed = 1.0;
    public double Speed
    {
        get => _speed;
        set => _speed = value switch { < 0.25 => 0.25, > 4.0 => 4.0, _ => value };
    }

    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wallflow");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception) { /* fichier corrompu → repartir de zéro */ }
        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
