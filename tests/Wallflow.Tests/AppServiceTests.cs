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

    private static readonly SlideshowSnapshot Snap = new(@"C:\Users\Me\Pictures\Slides", 600000, true);

    [Fact]
    public void Apply_FirstWallpaper_CapturesSlideshowOnce()
    {
        UseTempSettingsDir();
        var pm = new FakePlayerManager { SlideshowToReturn = Snap };
        var svc = new AppService(pm); // dossier temp neuf → LastWallpaper null → pas d'Apply au ctor

        svc.Apply(CreateTempMedia(".gif")); // transition aucun Wallpaper actif → actif

        Assert.Equal(1, pm.PauseSlideshowCalls);
    }

    [Fact]
    public void Apply_ImageChangeWhileActive_DoesNotRecapture()
    {
        UseTempSettingsDir();
        var pm = new FakePlayerManager { SlideshowToReturn = Snap };
        var svc = new AppService(pm);

        svc.Apply(CreateTempMedia(".gif")); // transition → capture
        svc.Apply(CreateTempMedia(".mp4")); // Wallpaper déjà actif → pas de recapture

        Assert.Equal(1, pm.PauseSlideshowCalls);
    }

    [Fact]
    public void RemoveWallpaper_RestoresCapturedSlideshow()
    {
        UseTempSettingsDir();
        var pm = new FakePlayerManager { SlideshowToReturn = Snap };
        var svc = new AppService(pm);
        svc.Apply(CreateTempMedia(".gif"));

        svc.RemoveWallpaper();

        Assert.Equal(1, pm.ResumeSlideshowCalls);
        Assert.Equal(Snap, pm.LastResumed);
    }

    [Fact]
    public void RemoveWallpaper_WithNoSlideshowActive_RestoresNothing()
    {
        UseTempSettingsDir();
        var pm = new FakePlayerManager { SlideshowToReturn = null }; // diaporama inactif à l'Apply
        var svc = new AppService(pm);
        svc.Apply(CreateTempMedia(".gif"));

        svc.RemoveWallpaper();

        Assert.Equal(0, pm.ResumeSlideshowCalls);
    }

    [Fact]
    public void RemoveWallpaper_ForgetsSnapshot_NoDoubleRestore()
    {
        UseTempSettingsDir();
        var pm = new FakePlayerManager { SlideshowToReturn = Snap };
        var svc = new AppService(pm);
        svc.Apply(CreateTempMedia(".gif"));

        svc.RemoveWallpaper();
        svc.RemoveWallpaper(); // plus de capture en mémoire → pas de seconde restauration

        Assert.Equal(1, pm.ResumeSlideshowCalls);
    }

    [Fact]
    public void Shutdown_RestoresCapturedSlideshow()
    {
        UseTempSettingsDir();
        var pm = new FakePlayerManager { SlideshowToReturn = Snap };
        var svc = new AppService(pm);
        svc.Apply(CreateTempMedia(".gif"));

        svc.Shutdown();

        Assert.Equal(1, pm.ResumeSlideshowCalls);
        Assert.Equal(Snap, pm.LastResumed);
    }

    [Fact]
    public void RemoveFromRecents_LeavesSlideshowUntouched()
    {
        UseTempSettingsDir();
        var pm = new FakePlayerManager { SlideshowToReturn = Snap };
        var svc = new AppService(pm);
        var a = CreateTempMedia(".gif");
        svc.Apply(a);

        svc.RemoveFromRecents(a);

        Assert.Equal(1, pm.PauseSlideshowCalls); // aucune capture en plus
        Assert.Equal(0, pm.ResumeSlideshowCalls); // aucune restauration
    }

    private sealed class FakePlayerManager : IPlayerManager
    {
        public Settings? LastApplied { get; private set; }
        public bool Cleared { get; private set; }
        public bool LoadCalled { get; private set; }

        // Diaporama Windows (issue 007) : la config renvoyée à la capture, et les compteurs
        // d'appels qui laissent les tests vérifier l'orchestration via le seam IPlayerManager.
        public SlideshowSnapshot? SlideshowToReturn { get; set; }
        public int PauseSlideshowCalls { get; private set; }
        public int ResumeSlideshowCalls { get; private set; }
        public SlideshowSnapshot? LastResumed { get; private set; }

        public void ApplySettings(Settings settings) => LastApplied = settings;
        public void Load(string path) => LoadCalled = true;
        public void PauseAll() { }
        public void ResumeAll() { }
        public void Rebuild() { }
        public void Clear() => Cleared = true;
        public void Dispose() { }
        public SlideshowSnapshot? PauseSlideshowIfActive() { PauseSlideshowCalls++; return SlideshowToReturn; }
        public void ResumeSlideshow(SlideshowSnapshot snapshot) { ResumeSlideshowCalls++; LastResumed = snapshot; }
    }
}
