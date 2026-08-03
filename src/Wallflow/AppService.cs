using System.IO;
using Microsoft.Win32;

namespace Wallflow;

/// <summary>
/// Chef d'orchestre et seul état mutable : wallpaper courant, récents, les deux drapeaux
/// de pause (manuelle / auto — indépendants : lever l'un ne lève jamais l'autre).
/// </summary>
public sealed class AppService
{
    public static readonly string[] SupportedExtensions =
        [".gif", ".webp", ".mp4", ".webm", ".mkv", ".png", ".jpg", ".jpeg", ".bmp"];

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public Settings Settings { get; }
    public event Action? StateChanged;

    private readonly IPlayerManager _players;
    private bool _manualPause;
    private bool _autoPause;

    public AppService() : this(new PlayerManager()) { }

    public AppService(IPlayerManager playerManager)
    {
        _players = playerManager;
        Settings = Settings.Load();
        WriteRunKey();
        WallpaperHost.Init();

        var monitor = new ActivityMonitor();
        monitor.ShouldPauseChanged += shouldPause =>
        {
            _autoPause = shouldPause && Settings.AutoPauseEnabled;
            ApplyPauseState();
        };
        SystemEvents.DisplaySettingsChanged += (_, _) => _players.Rebuild();

        // Mémorise les settings dans le manager avant le premier Load, pour qu'il
        // les applique aux players qu'il créera (sinon défauts mpv : muet, 1x, cover).
        _players.ApplySettings(Settings);
        if (Settings.LastWallpaper is { } last && File.Exists(last))
            Apply(last);
    }

    public bool ManualPause
    {
        get => _manualPause;
        set
        {
            _manualPause = value;
            ApplyPauseState();
            StateChanged?.Invoke();
        }
    }

    /// <summary>Récents dont le fichier existe encore (les supprimés sont purgés à la lecture).</summary>
    public IReadOnlyList<string> Recents => Settings.Recents.Where(File.Exists).ToList();

    /// <summary>Wallpaper actif = wallpaper courant persisté (nul après « Retirer le fond d'écran »). Pas de nouvel état.</summary>
    public string? ActiveWallpaper => Settings.LastWallpaper;

    public bool Apply(string path)
    {
        if (!File.Exists(path) || !SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
            return false;

        // Transition « aucun Wallpaper actif → actif » : coupe le diaporama Windows et garde sa
        // config. Un simple changement d'image (Wallpaper déjà actif) ne redéclenche pas de capture.
        if (Settings.LastWallpaper is null)
            Settings.SlideshowSnapshot = _players.PauseSlideshowIfActive();

        // Joue la version convertie si elle existe déjà ; sinon l'original tout de suite,
        // et bascule à chaud vers le mp4 dès que la conversion aboutit (si toujours actif).
        var cached = WallpaperCache.TryGet(path);
        _players.Load(cached ?? path);
        if (cached is null)
            WallpaperCache.ConvertAsync(path, converted =>
                // Le test « toujours actif » doit vivre dans le lambda dispatché : vérifié sur le
                // thread pool, un Apply intercalé serait écrasé par le mp4 converti périmé.
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (string.Equals(Settings.LastWallpaper, path, StringComparison.OrdinalIgnoreCase))
                        _players.Load(converted);
                }));
        Settings.LastWallpaper = path;
        Settings.Recents.Remove(path);
        Settings.Recents.Insert(0, path);
        if (Settings.Recents.Count > 10)
            Settings.Recents.RemoveRange(10, Settings.Recents.Count - 10);
        Settings.Save();
        StateChanged?.Invoke();
        return true;
    }

    /// <summary>« Retirer le fond d'écran » : rend le bureau natif, app vivante dans le tray. Ré-applicable via Récents.</summary>
    public void RemoveWallpaper()
    {
        ResumeCapturedSlideshow();
        Settings.LastWallpaper = null;
        _players.Clear();
        Settings.Save();
        StateChanged?.Invoke();
    }

    /// <summary>Restaure le diaporama Windows capturé (issu du Settings persisté, donc résilient au crash), puis l'oublie. No-op sinon.</summary>
    private void ResumeCapturedSlideshow()
    {
        if (Settings.SlideshowSnapshot is not { } snap) return;
        _players.ResumeSlideshow(snap);
        Settings.SlideshowSnapshot = null;
        Settings.Save();
    }

    /// <summary>« Retirer des récents » : ôte l'entrée persistée et notifie ; ne touche ni les players ni le wallpaper courant (même si c'est l'actif).</summary>
    public void RemoveFromRecents(string path)
    {
        Settings.Recents.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        Settings.Save();
        StateChanged?.Invoke();
    }

    public void SetAutoStart(bool enabled)
    {
        Settings.AutoStart = enabled;
        Settings.Save();
        WriteRunKey();
        StateChanged?.Invoke();
    }

    public void ApplyPlaybackSettings()
    {
        Settings.Save();
        _players.ApplySettings(Settings);
        StateChanged?.Invoke();
    }

    private void ApplyPauseState()
    {
        if (_manualPause || _autoPause) _players.PauseAll();
        else _players.ResumeAll();
    }

    /// <summary>Coupe l'écriture de la clé Run. Isolation des tests uniquement — sans ça,
    /// chaque dotnet test enregistre testhost.exe au démarrage de Windows (même pattern
    /// que Settings.DirOverride pour le settings.json).</summary>
    public static bool SkipRunKey { get; set; }

    /// <summary>Réécrite à chaque lancement : le zip portable peut être déplacé, la clé suit l'exe.</summary>
    private void WriteRunKey()
    {
        if (SkipRunKey) return;
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (Settings.AutoStart && Environment.ProcessPath is { } exe)
            key.SetValue("Wallflow", $"\"{exe}\" --tray");
        else
            key.DeleteValue("Wallflow", throwOnMissingValue: false);
    }

    public void Shutdown()
    {
        ResumeCapturedSlideshow();
        _players.Dispose();
    }
}
