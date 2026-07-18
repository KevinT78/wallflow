using System.Windows.Forms;

namespace Wallflow;

/// <summary>Un MpvPlayer par écran, même wallpaper partout. Reconstruit tout au changement d'écrans.</summary>
public sealed class PlayerManager : IDisposable
{
    private sealed record Entry(IntPtr Host, MpvPlayer Player);

    private readonly List<Entry> _entries = [];
    private string? _current;
    private bool _paused;

    public void Load(string path)
    {
        _current = path;
        EnsurePlayers();
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
        DisposePlayers();
        if (_current != null) Load(_current);
    }

    private void EnsurePlayers()
    {
        if (_entries.Count > 0) return;
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

    public void Dispose() => DisposePlayers();
}
