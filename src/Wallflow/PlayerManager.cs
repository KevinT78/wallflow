using System.Windows.Forms;

namespace Wallflow;

/// <summary>Un MpvPlayer par écran, même wallpaper partout. Reconstruit tout au changement d'écrans.</summary>
public sealed class PlayerManager : IPlayerManager, IDisposable
{
    private sealed record Entry(IntPtr Host, MpvPlayer Player);

    private readonly List<Entry> _entries = [];
    private string? _current;
    private bool _paused;
    private Settings? _settings;
    private string _screenSig = "";

    // La recréation d'un player mpv (contexte D3D11 dans le WorkerW) émet elle-même un
    // DisplaySettingsChanged → boucle infinie de Rebuild si on ne compare pas la config réelle.
    private static string ScreenSig() =>
        string.Join(";", Screen.AllScreens.Select(s => s.Bounds));

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
        DisposePlayers();
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
        if (_entries.Count > 0) return;
        _screenSig = ScreenSig();
        foreach (var screen in Screen.AllScreens)
        {
            var host = WallpaperHost.CreateHostFor(screen);
            _entries.Add(new Entry(host, new MpvPlayer(host)));
        }
    }

    private void DisposePlayers()
    {
        foreach (var e in _entries)
        {
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
            e.Player.ApplyVolume(settings.Volume, settings.Muted);
            e.Player.ApplyVideoFit(settings.VideoFit);
            e.Player.ApplyLoop(settings.Loop);
            e.Player.ApplySpeed(settings.Speed);
        }
    }

    // Délégation pure vers la primitive COM (live-only) : l'orchestration vit dans AppService.
    public SlideshowSnapshot? PauseSlideshowIfActive() => WallpaperHost.PauseSlideshowIfActive();
    public void ResumeSlideshow(SlideshowSnapshot snapshot) => WallpaperHost.ResumeSlideshow(snapshot);

    // Quitter : mêmes symptômes qu'un Clear (bureau blanc sinon), mais l'app se termine derrière.
    public void Dispose()
    {
        DisposePlayers();
        WallpaperHost.RestoreDesktop();
    }
}
