using System.Windows.Forms;
using System.Windows.Threading;

namespace Wallflow;

/// <summary>Un MpvPlayer par écran, même wallpaper partout. Reconstruit tout au changement d'écrans.</summary>
public sealed class PlayerManager : IPlayerManager, IDisposable
{
    private sealed record Entry(IntPtr Host, MpvPlayer Player);

    private readonly List<Entry> _entries = [];
    private readonly DispatcherTimer _hostWatchdog;
    private string? _current;
    private bool _paused;
    private Settings? _settings;
    private string _screenSig = "";

    /// <summary>Agrège les PlaybackError des MpvPlayer (émis depuis la thread d'événements mpv).</summary>
    public event Action<string>? PlaybackError;

    public PlayerManager()
    {
        // Filet de sécurité, sur le modèle du timer 2 s d'ActivityMonitor. Explorer peut réémettre
        // le WorkerW sans qu'aucun événement exploitable ne nous parvienne — constaté au réveil
        // d'une veille prolongée le 2026-08-20 : hosts détruits, process vivant, journal muet,
        // bureau noir jusqu'au prochain Apply manuel. Les deux chemins événementiels ne rattrapent
        // PAS ce cas : PowerModes.Resume → ResyncLight recharge dans les hosts morts, et
        // DisplaySettingsChanged → Rebuild sort tôt puisque la config d'écrans n'a pas bougé.
        _hostWatchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _hostWatchdog.Tick += (_, _) => EnsureHostsAlive();
        _hostWatchdog.Start();
    }

    // La recréation d'un player mpv (contexte D3D11 dans le WorkerW) émet elle-même un
    // DisplaySettingsChanged → boucle infinie de Rebuild si on ne compare pas la config réelle.
    private static string ScreenSig() =>
        string.Join(";", Screen.AllScreens.Select(s => s.Bounds));

    private bool HostsAlive() => WallpaperHost.HostsAlive(_entries.Select(e => e.Host).ToList());

    /// <summary>Tick du watchdog : recharge le wallpaper si les hosts ont été perdus. Le rechargement
    /// passe par EnsurePlayers, qui voit les hosts morts et recrée tout. No-op au cas nominal.</summary>
    private void EnsureHostsAlive()
    {
        // Une exception ici est fatale (Tick → Dispatcher → processus), comme dans
        // ActivityMonitor.Poll : une surveillance best-effort ne doit jamais tuer l'app.
        try
        {
            if (_current is { } path && _entries.Count > 0 && !HostsAlive()) Load(path);
        }
        catch (Exception ex)
        {
            Log.Warn($"Watchdog des hosts échoué ({ex.GetType().Name} : {ex.Message})");
        }
    }

    public void Load(string path)
    {
        _current = path;
        EnsurePlayers();
        // Réapplique les settings : les players fraîchement créés (démarrage, Rebuild)
        // partent sur les défauts figés du constructeur MpvPlayer, pas sur settings.json.
        if (_settings is { } s) ApplySettings(s);
        foreach (var e in _entries)
        {
            e.Player.Load(path);
            if (_paused) e.Player.Pause();
        }
    }

    public void PauseAll()
    {
        _paused = true;
        foreach (var e in _entries) e.Player.Pause();
    }

    public void ResumeAll()
    {
        _paused = false;
        foreach (var e in _entries) e.Player.Resume();
    }

    /// <summary>Écran branché/débranché : on jette tout et on recrée. Brutal mais rare.</summary>
    public void Rebuild()
    {
        if (ScreenSig() == _screenSig) return; // écrans inchangés : événement parasite
        Resync();
    }

    /// <summary>Reload forcé (veille/reprise, etc.) : même teardown qu'un Rebuild mais sans la
    /// garde de signature — on veut rejouer le wallpaper même si la config d'écrans n'a pas bougé.</summary>
    public void Resync()
    {
        DisposePlayers();
        if (_current != null) Load(_current);
    }

    /// <summary>Reload léger (résume après veille courte) : garde le contexte mpv et le host Win32
    /// vivants, recharge juste le fichier. Détruire/recréer le contexte à chaque résume coûte
    /// ~400ms de recompilation de shaders gpu-next (mesuré) que ce chemin évite entièrement.
    /// Réservé au résume système — un vrai changement d'écrans reste un `Resync()` classique.</summary>
    public void ResyncLight()
    {
        if (_current != null) Load(_current);
    }

    /// <summary>Retirer le fond d'écran : teardown des players + retour au bureau natif. L'app reste vivante.</summary>
    public void Clear()
    {
        DisposePlayers();
        _current = null;
        WallpaperHost.RestoreDesktop();
    }

    private void EnsurePlayers()
    {
        if (_entries.Count > 0)
        {
            if (HostsAlive()) return;
            // Explorer a réémis le WorkerW : nos hosts sont détruits ou orphelins. Recharger le
            // fichier dans ces players ne peint plus rien — bureau noir, et aucune erreur mpv pour
            // le signaler. Guard placé ici parce que tout ce qui (re)crée des players y passe :
            // Load, Resync, ResyncLight et le watchdog. Rebuild, lui, sort avant sur une ScreenSig()
            // inchangée — c'est justement pourquoi il ne rattrapait pas le bureau noir au réveil.
            Log.Warn("Hosts perdus (réémission du WorkerW) — recréation des players");
            DisposePlayers();
        }
        _screenSig = ScreenSig();
        foreach (var screen in Screen.AllScreens)
        {
            var host = WallpaperHost.CreateHostFor(screen);
            var player = new MpvPlayer(host);
            player.PlaybackError += OnPlayerError;
            _entries.Add(new Entry(host, player));
        }
    }

    private void OnPlayerError(string message) => PlaybackError?.Invoke(message);

    private void DisposePlayers()
    {
        foreach (var e in _entries)
        {
            e.Player.PlaybackError -= OnPlayerError;
            e.Player.Dispose();
            WallpaperHost.DestroyHost(e.Host);
        }
        _entries.Clear();
    }

    public void ApplySettings(Settings settings)
    {
        _settings = settings;
        foreach (var e in _entries)
        {
            e.Player.ApplyVideoFit(settings.VideoFit);
            e.Player.ApplyLoop(settings.Loop);
            e.Player.ApplySpeed(settings.Speed);
        }
    }

    // Délégation pure vers la primitive COM (live-only) : l'orchestration vit dans AppService.
    public SlideshowSnapshot? PauseSlideshowIfActive() => WallpaperHost.PauseSlideshowIfActive();
    public void ResumeSlideshow(SlideshowSnapshot snapshot) => WallpaperHost.ResumeSlideshow(snapshot);
    public bool EnsureSlideshowPaused() => WallpaperHost.EnsureSlideshowPaused();

    // Quitter : mêmes symptômes qu'un Clear (bureau blanc sinon), mais l'app se termine derrière.
    public void Dispose()
    {
        _hostWatchdog.Stop();
        DisposePlayers();
        WallpaperHost.RestoreDesktop();
    }
}
