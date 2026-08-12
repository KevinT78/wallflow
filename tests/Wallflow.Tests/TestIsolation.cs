using System.IO;

namespace Wallflow.Tests;

/// <summary>
/// Isole un test du réel : settings.json (dossier temp unique), clé registre Run, spawn ffmpeg,
/// écritures de logs. Restaure l'état précédent au dispose. La parallélisation xUnit est
/// désactivée (AssemblyInfo) car ces statiques sont globales au process.
/// </summary>
public sealed class TestIsolation : IDisposable
{
    private readonly string? _prevDir;
    private readonly bool _prevSkipRunKey;
    private readonly bool _prevCacheDisabled;
    private readonly string? _prevCacheDir;
    private readonly bool _prevLogEnabled;
    private readonly bool _prevMonitorDisabled;

    public TestIsolation()
    {
        _prevDir = Settings.DirOverride;
        _prevSkipRunKey = AppService.SkipRunKey;
        _prevCacheDisabled = WallpaperCache.Disabled;
        _prevCacheDir = WallpaperCache.DirOverride;
        _prevLogEnabled = Log.Enabled;
        _prevMonitorDisabled = ActivityMonitor.Disabled;

        Settings.DirOverride = Path.Combine(Path.GetTempPath(), "WallflowTests_" + Guid.NewGuid());
        AppService.SkipRunKey = true;
        WallpaperCache.Disabled = true;
        // Toujours posé, même si Disabled coupe la plupart des accès : un test qui repasse
        // Disabled à false (pour tester PruneOrphans) ne doit jamais toucher le vrai cache utilisateur.
        WallpaperCache.DirOverride = Path.Combine(Path.GetTempPath(), "WallflowTests_Cache_" + Guid.NewGuid());
        Log.Enabled = false;
        ActivityMonitor.Disabled = true;
    }

    /// <summary>Fichier réel (vide) dans le dossier temp isolé : suffit à Apply (File.Exists +
    /// extension), le fake PlayerManager ne le décode jamais.</summary>
    public string CreateTempMedia(string ext)
    {
        Directory.CreateDirectory(Settings.DirOverride!);
        var path = Path.Combine(Settings.DirOverride!, Guid.NewGuid() + ext);
        File.WriteAllBytes(path, []);
        return path;
    }

    public void Dispose()
    {
        Settings.DirOverride = _prevDir;
        AppService.SkipRunKey = _prevSkipRunKey;
        WallpaperCache.Disabled = _prevCacheDisabled;
        WallpaperCache.DirOverride = _prevCacheDir;
        Log.Enabled = _prevLogEnabled;
        ActivityMonitor.Disabled = _prevMonitorDisabled;
    }
}
