using System.IO;
using Xunit;

namespace Wallflow.Tests;

public class AppServiceTests
{
    // Isole Settings.Load/Save d'un dossier temporaire : sans ça, RemoveWallpaper
    // (LastWallpaper = null + Save) écraserait le settings.json réel de l'utilisateur.
    // Coupe aussi l'écriture de la clé Run : sans ça, chaque run de tests enregistre
    // testhost.exe dans le démarrage Windows de l'utilisateur (AutoStart est true par défaut).
    private static void UseTempSettingsDir()
    {
        Settings.DirOverride = Path.Combine(Path.GetTempPath(), "WallflowTests_" + Guid.NewGuid());
        AppService.SkipRunKey = true;
        WallpaperCache.Disabled = true; // sans ça, Apply(.gif) spawnerait un ffmpeg par test
    }

    // Fichier réel (vide) dans le dossier temp isolé : suffit à Apply (File.Exists + extension),
    // le fake PlayerManager ne le décode jamais.
    private static string CreateTempMedia(string ext)
    {
        Directory.CreateDirectory(Settings.DirOverride!);
        var path = Path.Combine(Settings.DirOverride!, Guid.NewGuid() + ext);
        File.WriteAllBytes(path, []);
        return path;
    }

    [Fact]
    public void ActiveWallpaper_ReflectsCurrentWallpaper_AndClearsOnRemove()
    {
        UseTempSettingsDir();
        var svc = new AppService(new FakePlayerManager());
        var file = CreateTempMedia(".gif");

        svc.Apply(file);
        Assert.Equal(file, svc.ActiveWallpaper);

        svc.RemoveWallpaper();
        Assert.Null(svc.ActiveWallpaper);
    }

    [Fact]
    public void Constructor_PushesSettingsToPlayerManager()
    {
        UseTempSettingsDir();
        var pm = new FakePlayerManager();
        _ = new AppService(pm);

        Assert.NotNull(pm.LastApplied);
    }

    [Fact]
    public void ApplyPlaybackSettings_PropagatesToPlayerManager()
    {
        UseTempSettingsDir();
        var pm = new FakePlayerManager();
        var svc = new AppService(pm);
        svc.Settings.Volume = 50;
        svc.ApplyPlaybackSettings();

        Assert.Equal(50, pm.LastApplied?.Volume);
    }

    [Fact]
    public void RemoveWallpaper_ClearsPlayersAndForgetsLastWallpaper()
    {
        UseTempSettingsDir();
        var pm = new FakePlayerManager();
        var svc = new AppService(pm);
        svc.Settings.LastWallpaper = @"C:\fake\wallpaper.gif";

        svc.RemoveWallpaper();

        Assert.Null(svc.Settings.LastWallpaper);
        Assert.True(pm.Cleared);
    }

    [Fact]
    public void Constructor_WithNoLastWallpaper_NeverCallsLoad()
    {
        // Décision B : "retiré" = LastWallpaper absent. Dossier temp tout neuf → pas de
        // settings.json → LastWallpaper == null au démarrage → aucun Load ne doit partir.
        UseTempSettingsDir();
        var pm = new FakePlayerManager();
        _ = new AppService(pm);

        Assert.False(pm.LoadCalled);
    }

    [Fact]
    public void RemoveFromRecents_RemovesEntry_Persists_AndNotifies()
    {
        UseTempSettingsDir();
        var svc = new AppService(new FakePlayerManager());
        var a = CreateTempMedia(".gif");
        var b = CreateTempMedia(".mp4");
        svc.Apply(a);
        svc.Apply(b);
        var notified = false;
        svc.StateChanged += () => notified = true;

        svc.RemoveFromRecents(a);

        Assert.DoesNotContain(a, svc.Recents);
        Assert.True(notified);
        Assert.DoesNotContain(a, Settings.Load().Recents); // persisté
    }

    [Fact]
    public void RemoveFromRecents_MatchesPathCaseInsensitively()
    {
        UseTempSettingsDir();
        var svc = new AppService(new FakePlayerManager());
        var a = CreateTempMedia(".gif");
        svc.Apply(a);

        svc.RemoveFromRecents(a.ToUpperInvariant()); // chemins Windows insensibles à la casse

        Assert.Empty(svc.Recents);
    }

    [Fact]
    public void RemoveFromRecents_ActiveEntry_LeavesPlayersAndCurrentWallpaperUntouched()
    {
        UseTempSettingsDir();
        var pm = new FakePlayerManager();
        var svc = new AppService(pm);
        var a = CreateTempMedia(".gif");
        svc.Apply(a); // a devient le wallpaper actif

        svc.RemoveFromRecents(a);

        Assert.DoesNotContain(a, svc.Recents); // retiré de la grille
        Assert.Equal(a, svc.ActiveWallpaper);  // mais continue de jouer
        Assert.False(pm.Cleared);              // players intouchés
    }

    [Fact]
    public void Constructor_UnderTestIsolation_NeverTouchesRunRegistryKey()
    {
        UseTempSettingsDir();
        const string runPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(runPath);
        var before = key?.GetValue("Wallflow");

        _ = new AppService(new FakePlayerManager()); // AutoStart=true par défaut → écrirait testhost.exe

        using var after = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(runPath);
        Assert.Equal(before, after?.GetValue("Wallflow"));
    }

    private sealed class FakePlayerManager : IPlayerManager
    {
        public Settings? LastApplied { get; private set; }
        public bool Cleared { get; private set; }
        public bool LoadCalled { get; private set; }

        public void ApplySettings(Settings settings) => LastApplied = settings;
        public void Load(string path) => LoadCalled = true;
        public void PauseAll() { }
        public void ResumeAll() { }
        public void Rebuild() { }
        public void Clear() => Cleared = true;
        public void Dispose() { }
    }
}
