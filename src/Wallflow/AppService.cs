using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
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
    private const string WakeTaskName = "Wallflow_WakeRelaunch";

    /// <summary>Clé de déduplication de la grille des Récents : changée si et seulement si la liste
    /// des récents ou le wallpaper actif bouge. Les chemins NTFS ne peuvent pas contenir \n ni \0,
    /// donc aucune collision entre états différents.</summary>
    public static string GridKey(IReadOnlyList<string> recents, string? activeWallpaper) =>
        string.Join("\n", recents) + "\0" + activeWallpaper;

    public Settings Settings { get; }
    public event Action? StateChanged;

    /// <summary>Forwardé depuis les players (échec de lecture mpv) ; le consommateur (fenêtre)
    /// le marshale vers le thread UI — ici l'événement arrive sur la thread d'événements mpv.</summary>
    public event Action<string>? PlaybackError;

    private readonly IPlayerManager _players;
    private bool _manualPause;
    private bool _autoPause;

    /// <summary>Vrai dès que les players affichent un Wallpaper dans ce process. Détection runtime de
    /// la transition « aucun Wallpaper actif → actif » — le persisté Settings.LastWallpaper ne l'est
    /// pas : au démarrage, la restauration retrouve un LastWallpaper déjà non-null alors qu'aucun
    /// Wallpaper n'est encore affiché (sinon le diaporama, relancé par le Shutdown précédent, ne
    /// serait jamais recoupé).</summary>
    private bool _wallpaperActive;

    public AppService() : this(new PlayerManager()) { }

    public AppService(IPlayerManager playerManager)
    {
        _players = playerManager;
        _players.PlaybackError += message => PlaybackError?.Invoke(message);
        Settings = Settings.Load();
        WriteRunKey();
        WallpaperHost.Init();

        var monitor = new ActivityMonitor();
        monitor.ShouldPauseChanged += shouldPause =>
        {
            _autoPause = shouldPause && Settings.AutoPauseEnabled;
            ApplyPauseState();
        };

        // Ces événements système arrivent sur des threads système ; PlayerManager n'est pas
        // thread-safe → tout est marshallé vers le thread UI avant de toucher aux players.
        SystemEvents.DisplaySettingsChanged += (_, _) => OnUiThread(_players.Rebuild);
        SystemEvents.PowerModeChanged += (_, e) =>
        {
            // Après une veille, mpv peut rester figé : on force un reload du wallpaper courant
            // (replay + réapplication des réglages/pause), signature d'écrans ignorée.
            if (e.Mode == PowerModes.Resume)
                OnUiThread(_players.Resync);
        };

        // Mémorise les settings dans le manager avant le premier Load, pour qu'il
        // les applique aux players qu'il créera (sinon défauts mpv : muet, 1x, cover).
        _players.ApplySettings(Settings);
        if (Settings.LastWallpaper is { } last && File.Exists(last))
        {
            Log.Info($"Restauration du dernier wallpaper au démarrage : {last}");
            Apply(last);
        }
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
        Log.Info($"Apply : {path}");

        // Transition « aucun Wallpaper actif → actif » : coupe le diaporama Windows et garde sa
        // config. Un simple changement d'image (Wallpaper déjà actif) ne redéclenche pas de capture.
        // Détection par état runtime, pas par Settings.LastWallpaper persisté (voir _wallpaperActive).
        if (!_wallpaperActive)
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
        _wallpaperActive = true;
        StateChanged?.Invoke();
        return true;
    }

    /// <summary>« Retirer le fond d'écran » : rend le bureau natif, app vivante dans le tray. Ré-applicable via Récents.</summary>
    public void RemoveWallpaper()
    {
        ResumeCapturedSlideshow();
        Settings.LastWallpaper = null;
        _wallpaperActive = false;
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

    public void ApplyPlaybackSettings(bool save = true)
    {
        if (save) Settings.Save();
        _players.ApplySettings(Settings);
        StateChanged?.Invoke();
    }

    private void ApplyPauseState()
    {
        if (_manualPause || _autoPause) _players.PauseAll();
        else _players.ResumeAll();
    }

    /// <summary>Marshale vers le thread UI (les événements SystemEvents arrivent sur des threads
    /// système). Sans Application (tests, shutdown) : no-op sûr.</summary>
    private void OnUiThread(Action action)
    {
        var app = System.Windows.Application.Current;
        if (app == null) return;
        try { app.Dispatcher.Invoke(action); }
        catch (InvalidOperationException) { /* Dispatcher arrêté pendant le shutdown */ }
    }

    /// <summary>Coupe l'écriture de la clé Run. Isolation des tests uniquement — sans ça,
    /// chaque dotnet test enregistre testhost.exe au démarrage de Windows (même pattern
    /// que Settings.DirOverride pour le settings.json).</summary>
    public static bool SkipRunKey { get; set; }

    /// <summary>Réécrite à chaque lancement : le zip portable peut être déplacé, la clé suit l'exe.</summary>
    private void WriteRunKey()
    {
        if (SkipRunKey) return;
        var exe = Environment.ProcessPath;
        using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
        {
            if (Settings.AutoStart && exe is { } path)
                key.SetValue("Wallflow", $"\"{path}\" --tray");
            else
                key.DeleteValue("Wallflow", throwOnMissingValue: false);
        }
        WriteWakeTask(exe);
    }

    /// <summary>Construit la commande schtasks (create/delete) pour la tâche de relance au réveil.
    /// La clé Run ne se rejoue qu'à l'ouverture de session : si Windows a tué le process pendant une
    /// veille prolongée, rien ne le relance sans ce déclencheur — d'où l'event Power-Troubleshooter
    /// EventID 1 (sortie de veille/veille prolongée), au lieu du seul /sc onlogon.</summary>
    public static string BuildWakeTaskArgs(string exePath, bool enabled) =>
        enabled
            // --wake-relaunch (voir App.OnStartup) : si une instance tourne déjà, sortir sans
            // réveiller la fenêtre — sinon chaque sortie de veille rouvrirait l'UI sans raison.
            ? $"/create /tn {WakeTaskName} /tr \"\\\"{exePath}\\\" --tray --wake-relaunch\" /sc onevent /ec System " +
              "/mo \"*[System[Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and EventID=1]]\" /f"
            : $"/delete /tn {WakeTaskName} /f";

    /// <summary>Enregistre/retire la tâche planifiée. Best-effort : schtasks absent ou en échec ne
    /// doit pas empêcher le démarrage de l'app (même logique de tolérance que ActivityMonitor.Poll).</summary>
    private void WriteWakeTask(string? exe)
    {
        var enabled = Settings.AutoStart && exe is not null;
        try
        {
            var psi = new ProcessStartInfo("schtasks", BuildWakeTaskArgs(exe ?? "", enabled))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit();
        }
        catch (Exception ex) { Log.Warn($"schtasks (tâche de réveil) a échoué : {ex.Message}"); }
    }

    public void Shutdown()
    {
        ResumeCapturedSlideshow();
        _wallpaperActive = false;
        _players.Dispose();
    }
}
