using System.IO;
using Xunit;

namespace Wallflow.Tests;

public class AppServiceTests
{
    // Isole Settings.Load/Save d'un dossier temporaire : sans ça, RemoveWallpaper
    // (LastWallpaper = null + Save) écraserait le settings.json réel de l'utilisateur.
    private static void UseTempSettingsDir() =>
        Settings.DirOverride = Path.Combine(Path.GetTempPath(), "WallflowTests_" + Guid.NewGuid());

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
