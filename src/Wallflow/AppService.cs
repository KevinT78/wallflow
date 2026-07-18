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

    public bool Apply(string path)
    {
        if (!File.Exists(path) || !SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
            return false;

        _players.Load(path);
        Settings.LastWallpaper = path;
        Settings.Recents.Remove(path);
        Settings.Recents.Insert(0, path);
        if (Settings.Recents.Count > 10)
            Settings.Recents.RemoveRange(10, Settings.Recents.Count - 10);
        Settings.Save();
        StateChanged?.Invoke();
        return true;
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

    /// <summary>Réécrite à chaque lancement : le zip portable peut être déplacé, la clé suit l'exe.</summary>
    private void WriteRunKey()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (Settings.AutoStart && Environment.ProcessPath is { } exe)
            key.SetValue("Wallflow", $"\"{exe}\" --tray");
        else
            key.DeleteValue("Wallflow", throwOnMissingValue: false);
    }

    public void Shutdown() => _players.Dispose();
}
