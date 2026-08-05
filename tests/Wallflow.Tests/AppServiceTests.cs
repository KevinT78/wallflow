using System.IO;
using Xunit;

namespace Wallflow.Tests;

public class AppServiceTests
{
    [Fact]
    public void ActiveWallpaper_ReflectsCurrentWallpaper_AndClearsOnRemove()
    {
        using var iso = new TestIsolation();
        var svc = new AppService(new FakePlayerManager());
        var file =iso.CreateTempMedia(".gif");

        svc.Apply(file);
        Assert.Equal(file, svc.ActiveWallpaper);

        svc.RemoveWallpaper();
        Assert.Null(svc.ActiveWallpaper);
    }

    [Fact]
    public void Constructor_PushesSettingsToPlayerManager()
    {
        using var iso = new TestIsolation();
        var pm = new FakePlayerManager();
        _ = new AppService(pm);

        Assert.NotNull(pm.LastApplied);
    }

    [Fact]
    public void ApplyPlaybackSettings_PropagatesToPlayerManager()
    {
        using var iso = new TestIsolation();
        var pm = new FakePlayerManager();
        var svc = new AppService(pm);
        svc.Settings.Volume = 50;
        svc.ApplyPlaybackSettings();

        Assert.Equal(50, pm.LastApplied?.Volume);
    }

    [Fact]
    public void ApplyPlaybackSettings_WithoutSave_PropagatesButDoesNotPersist()
    {
        using var iso = new TestIsolation();
        var pm = new FakePlayerManager();
        var svc = new AppService(pm);
        svc.Settings.Volume = 37;
        svc.ApplyPlaybackSettings(save: false);

        Assert.Equal(37, pm.LastApplied?.Volume);     // appliqué aux players à chaud
        Assert.Equal(100, Settings.Load().Volume);     // mais rien d'écrit sur le disque (défauts)
    }

    [Fact]
    public void RemoveWallpaper_ClearsPlayersAndForgetsLastWallpaper()
    {
        using var iso = new TestIsolation();
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
        using var iso = new TestIsolation();
        var pm = new FakePlayerManager();
        _ = new AppService(pm);

        Assert.False(pm.LoadCalled);
    }

    [Fact]
    public void RemoveFromRecents_RemovesEntry_Persists_AndNotifies()
    {
        using var iso = new TestIsolation();
        var svc = new AppService(new FakePlayerManager());
        var a =iso.CreateTempMedia(".gif");
        var b =iso.CreateTempMedia(".mp4");
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
        using var iso = new TestIsolation();
        var svc = new AppService(new FakePlayerManager());
        var a =iso.CreateTempMedia(".gif");
        svc.Apply(a);

        svc.RemoveFromRecents(a.ToUpperInvariant()); // chemins Windows insensibles à la casse

        Assert.Empty(svc.Recents);
    }

    [Fact]
    public void RemoveFromRecents_ActiveEntry_LeavesPlayersAndCurrentWallpaperUntouched()
    {
        using var iso = new TestIsolation();
        var pm = new FakePlayerManager();
        var svc = new AppService(pm);
        var a =iso.CreateTempMedia(".gif");
        svc.Apply(a); // a devient le wallpaper actif

        svc.RemoveFromRecents(a);

        Assert.DoesNotContain(a, svc.Recents); // retiré de la grille
        Assert.Equal(a, svc.ActiveWallpaper);  // mais continue de jouer
        Assert.False(pm.Cleared);              // players intouchés
    }

    [Fact]
    public void Constructor_UnderTestIsolation_NeverTouchesRunRegistryKey()
    {
        using var iso = new TestIsolation();
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
        using var iso = new TestIsolation();
        var pm = new FakePlayerManager { SlideshowToReturn = Snap };
        var svc = new AppService(pm); // dossier temp neuf → LastWallpaper null → pas d'Apply au ctor

        svc.Apply(iso.CreateTempMedia(".gif")); // transition aucun Wallpaper actif → actif

        Assert.Equal(1, pm.PauseSlideshowCalls);
    }

    [Fact]
    public void Constructor_RestoringLastWallpaper_CapturesSlideshow()
    {
        // Bug de restauration : au démarrage, Settings.LastWallpaper est déjà non-null (persisté)
        // alors qu'aucun Wallpaper n'est encore affiché. La transition aucun→actif doit quand même
        // couper le diaporama — sinon le Shutdown précédent l'a relancé et il repique le wallpaper.
        using var iso = new TestIsolation();
        var pm = new FakePlayerManager { SlideshowToReturn = Snap };
        var file = iso.CreateTempMedia(".gif");
        new Settings { LastWallpaper = file }.Save(); // settings.json persisté simulant une session passée

        _ = new AppService(pm); // le ctor restaure file → transition aucun→actif

        Assert.Equal(1, pm.PauseSlideshowCalls);
        Assert.Equal(Snap, Settings.Load().SlideshowSnapshot); // capture persistée, restauration possible
    }

    [Fact]
    public void Constructor_RestoringLastWallpaper_ThenRemove_RestoresSlideshow()
    {
        using var iso = new TestIsolation();
        var pm = new FakePlayerManager { SlideshowToReturn = Snap };
        var file = iso.CreateTempMedia(".gif");
        new Settings { LastWallpaper = file }.Save();

        var svc = new AppService(pm);
        Assert.Equal(1, pm.PauseSlideshowCalls); // capture à la restauration

        svc.RemoveWallpaper();

        Assert.Equal(1, pm.ResumeSlideshowCalls); // et restauration symétrique au retrait
        Assert.Equal(Snap, pm.LastResumed);
    }

    [Fact]
    public void Apply_ImageChangeWhileActive_DoesNotRecapture()
    {
        using var iso = new TestIsolation();
        var pm = new FakePlayerManager { SlideshowToReturn = Snap };
        var svc = new AppService(pm);

        svc.Apply(iso.CreateTempMedia(".gif")); // transition → capture
        svc.Apply(iso.CreateTempMedia(".mp4")); // Wallpaper déjà actif → pas de recapture

        Assert.Equal(1, pm.PauseSlideshowCalls);
    }

    [Fact]
    public void RemoveWallpaper_RestoresCapturedSlideshow()
    {
        using var iso = new TestIsolation();
        var pm = new FakePlayerManager { SlideshowToReturn = Snap };
        var svc = new AppService(pm);
        svc.Apply(iso.CreateTempMedia(".gif"));

        svc.RemoveWallpaper();

        Assert.Equal(1, pm.ResumeSlideshowCalls);
        Assert.Equal(Snap, pm.LastResumed);
    }

    [Fact]
    public void RemoveWallpaper_WithNoSlideshowActive_RestoresNothing()
    {
        using var iso = new TestIsolation();
        var pm = new FakePlayerManager { SlideshowToReturn = null }; // diaporama inactif à l'Apply
        var svc = new AppService(pm);
        svc.Apply(iso.CreateTempMedia(".gif"));

        svc.RemoveWallpaper();

        Assert.Equal(0, pm.ResumeSlideshowCalls);
    }

    [Fact]
    public void RemoveWallpaper_ForgetsSnapshot_NoDoubleRestore()
    {
        using var iso = new TestIsolation();
        var pm = new FakePlayerManager { SlideshowToReturn = Snap };
        var svc = new AppService(pm);
        svc.Apply(iso.CreateTempMedia(".gif"));

        svc.RemoveWallpaper();
        svc.RemoveWallpaper(); // plus de capture en mémoire → pas de seconde restauration

        Assert.Equal(1, pm.ResumeSlideshowCalls);
    }

    [Fact]
    public void RemoveWallpaper_WithStaleSnapshot_RestoresAndClears()
    {
        using var iso = new TestIsolation();
        var pm = new FakePlayerManager();
        var svc = new AppService(pm);

        svc.Settings.SlideshowSnapshot = Snap; // simule un crash : snapshot persisté mais perdu en mémoire

        svc.RemoveWallpaper();

        Assert.Equal(1, pm.ResumeSlideshowCalls);
        Assert.Equal(Snap, pm.LastResumed);
        Assert.Null(svc.Settings.SlideshowSnapshot);
    }

    [Fact]
    public void Shutdown_RestoresCapturedSlideshow()
    {
        using var iso = new TestIsolation();
        var pm = new FakePlayerManager { SlideshowToReturn = Snap };
        var svc = new AppService(pm);
        svc.Apply(iso.CreateTempMedia(".gif"));

        svc.Shutdown();

        Assert.Equal(1, pm.ResumeSlideshowCalls);
        Assert.Equal(Snap, pm.LastResumed);
    }

    [Fact]
    public void RemoveFromRecents_LeavesSlideshowUntouched()
    {
        using var iso = new TestIsolation();
        var pm = new FakePlayerManager { SlideshowToReturn = Snap };
        var svc = new AppService(pm);
        var a =iso.CreateTempMedia(".gif");
        svc.Apply(a);

        Assert.Equal(1, pm.PauseSlideshowCalls); // aucune capture en plus
        Assert.Equal(0, pm.ResumeSlideshowCalls); // aucune restauration
    }

    [Fact]
    public void PlaybackError_ForwardedFromPlayerManager()
    {
        using var iso = new TestIsolation();
        var pm = new FakePlayerManager();
        var svc = new AppService(pm);
        string? received = null;
        svc.PlaybackError += message => received = message;

        pm.RaisePlaybackError("Format non décodable");

        Assert.Equal("Format non décodable", received);
    }

    [Fact]
    public void GridKey_ChangesWithOrdering()
    {
        Assert.NotEqual(AppService.GridKey(["a", "b"], null), AppService.GridKey(["b", "a"], null));
    }

    [Fact]
    public void GridKey_ChangesWithActiveWallpaper()
    {
        Assert.NotEqual(AppService.GridKey(["a"], null), AppService.GridKey(["a"], "a"));
    }

    [Fact]
    public void GridKey_ChangesWithMembership()
    {
        Assert.NotEqual(AppService.GridKey(["a"], null), AppService.GridKey(["a", "b"], null));
    }

    [Fact]
    public void GridKey_IsStableForIdenticalState()
    {
        Assert.Equal(AppService.GridKey(["a", "b"], "a"), AppService.GridKey(["a", "b"], "a"));
    }

    [Fact]
    public void BuildWakeTaskArgs_Enabled_CreatesTaskOnResumeFromHibernation()
    {
        var args = AppService.BuildWakeTaskArgs(@"C:\Wallflow\wallflow.exe", enabled: true);

        Assert.Contains("/create", args);
        Assert.Contains("Wallflow_WakeRelaunch", args);
        Assert.Contains(@"C:\Wallflow\wallflow.exe", args);
        Assert.Contains("--tray", args);
        // --wake-relaunch : App.OnStartup ne réveille pas la fenêtre d'une instance déjà active
        // pour ce lancement précis (sinon chaque sortie de veille rouvrirait l'UI sans raison).
        Assert.Contains("--wake-relaunch", args);
        // Power-Troubleshooter EventID 1 = système sorti de veille/veille prolongée (System log).
        Assert.Contains("Microsoft-Windows-Power-Troubleshooter", args);
        Assert.Contains("EventID=1", args);
    }

    [Fact]
    public void BuildWakeTaskArgs_Disabled_DeletesTask()
    {
        var args = AppService.BuildWakeTaskArgs(@"C:\Wallflow\wallflow.exe", enabled: false);

        Assert.Contains("/delete", args);
        Assert.Contains("Wallflow_WakeRelaunch", args);
    }

    private sealed class FakePlayerManager : IPlayerManager
    {
        public Settings? LastApplied { get; private set; }
        public bool Cleared { get; private set; }
        public bool LoadCalled { get; private set; }
        public event Action<string>? PlaybackError;
        public void RaisePlaybackError(string message) => PlaybackError?.Invoke(message);

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
        public void Resync() { }
        public void Clear() => Cleared = true;
        public void Dispose() { }
        public SlideshowSnapshot? PauseSlideshowIfActive() { PauseSlideshowCalls++; return SlideshowToReturn; }
        public void ResumeSlideshow(SlideshowSnapshot snapshot) { ResumeSlideshowCalls++; LastResumed = snapshot; }
    }
}
