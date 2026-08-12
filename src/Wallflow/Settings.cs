using System.IO;
using System.Text.Json;

namespace Wallflow;

public class Settings
{
    public string? LastWallpaper { get; set; }
    public List<string> Recents { get; set; } = [];
    public bool AutoStart { get; set; } = true;
    public bool AutoPauseEnabled { get; set; } = true;

    private string _videoFit = "cover";
    public string VideoFit
    {
        get => _videoFit;
        set => _videoFit = value is "cover" or "fit" or "fill" ? value : "cover";
    }

    public bool Loop { get; set; } = true;

    /// <summary>Snapshot du diaporama Windows capturé, persisté pour résilience au crash (issue 008).</summary>
    public SlideshowSnapshot? SlideshowSnapshot { get; set; }

    private double _speed = 1.0;
    public double Speed
    {
        get => _speed;
        set => _speed = value switch { < 0.25 => 0.25, > 4.0 => 4.0, _ => value };
    }

    /// <summary>Remplace le dossier %LocalAppData%\Wallflow pour Load/Save. Isolation des tests uniquement.</summary>
    public static string? DirOverride { get; set; }

    private static string Dir => DirOverride ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wallflow");
    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception ex) { Log.Warn($"settings.json illisible/corrompu, reprise à zéro ({ex.Message})"); }
        return new Settings();
    }

    public void Save()
    {
        // Purge les récents dont le fichier a disparu (écart relevé en doc : la grille les filtrait
        // déjà à l'affichage, mais le JSON gardait les chemins morts). Un réseau temporairement
        // injoignable perdrait l'entrée — assumé, comme le filtrage d'affichage existant.
        Recents.RemoveAll(p => !File.Exists(p));

        Directory.CreateDirectory(Dir);
        // Écriture atomique : temp puis rename par-dessus l'original, flush durable avant.
        // Un crash en plein WriteAllText corrompait settings.json (perte des récents/réglages).
        var tmp = FilePath + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(fs))
        {
            writer.Write(JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            writer.Flush();
            fs.Flush(true);
        }
        File.Move(tmp, FilePath, overwrite: true);
    }
}
