using System.IO;
using System.Text.Json;

namespace Wallflow;

public class Settings
{
    public string? LastWallpaper { get; set; }
    public List<string> Recents { get; set; } = [];
    public bool AutoStart { get; set; } = true;
    public bool AutoPauseEnabled { get; set; } = true;

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
