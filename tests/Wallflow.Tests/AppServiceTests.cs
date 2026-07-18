using Xunit;

namespace Wallflow.Tests;

public class AppServiceTests
{
    [Fact]
    public void Constructor_PushesSettingsToPlayerManager()
    {
        var pm = new FakePlayerManager();
        _ = new AppService(pm);

        Assert.NotNull(pm.LastApplied);
    }

    [Fact]
    public void ApplyPlaybackSettings_PropagatesToPlayerManager()
    {
        var pm = new FakePlayerManager();
        var svc = new AppService(pm);
        svc.Settings.Volume = 50;
        svc.ApplyPlaybackSettings();

        Assert.Equal(50, pm.LastApplied?.Volume);
    }

    private sealed class FakePlayerManager : IPlayerManager
    {
        public Settings? LastApplied { get; private set; }

        public void ApplySettings(Settings settings) => LastApplied = settings;
        public void Load(string path) { }
        public void PauseAll() { }
        public void ResumeAll() { }
        public void Rebuild() { }
        public void Dispose() { }
    }
}
