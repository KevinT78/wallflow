using System.IO;
using System.Text.Json;
using Xunit;

namespace Wallflow.Tests;

public class SettingsTests
{
    [Fact]
    public void Defaults()
    {
        var s = new Settings();
        Assert.Equal(100, s.Volume);
        Assert.False(s.Muted);
        Assert.Equal("cover", s.VideoFit);
        Assert.True(s.Loop);
        Assert.Equal(1.0, s.Speed);
    }

    [Fact]
    public void RoundTrip()
    {
        var s = new Settings
        {
            Volume = 75,
            Muted = true,
            VideoFit = "fill",
            Loop = false,
            Speed = 2.5,
            AutoStart = false,
            AutoPauseEnabled = false,
            SlideshowSnapshot = new(@"C:\Users\Me\Pictures\Slides", 600000, true),
        };
        var json = JsonSerializer.Serialize(s);
        var deserialized = JsonSerializer.Deserialize<Settings>(json)!;

        Assert.Equal(75, deserialized.Volume);
        Assert.True(deserialized.Muted);
        Assert.Equal("fill", deserialized.VideoFit);
        Assert.False(deserialized.Loop);
        Assert.Equal(2.5, deserialized.Speed);
        Assert.False(deserialized.AutoStart);
        Assert.False(deserialized.AutoPauseEnabled);
        Assert.NotNull(deserialized.SlideshowSnapshot);
        Assert.Equal(@"C:\Users\Me\Pictures\Slides", deserialized.SlideshowSnapshot!.FolderPath);
        Assert.Equal(600000u, deserialized.SlideshowSnapshot.IntervalMs);
        Assert.True(deserialized.SlideshowSnapshot.Shuffle);
    }

    [Fact]
    public void ClampVolume()
    {
        var s = new Settings { Volume = 150 };
        Assert.Equal(100, s.Volume);
    }

    [Fact]
    public void ClampSpeed()
    {
        var s = new Settings { Speed = 10.0 };
        Assert.Equal(4.0, s.Speed);
    }

    [Fact]
    public void Save_IsAtomic_NoTempLeftBehind()
    {
        using var iso = new TestIsolation();
        var s = new Settings { Volume = 42, LastWallpaper = @"C:\x\y.gif" };
        s.Save();

        Assert.False(File.Exists(Path.Combine(Settings.DirOverride!, "settings.json.tmp")));
        var loaded = Settings.Load();
        Assert.Equal(42, loaded.Volume);
        Assert.Equal(@"C:\x\y.gif", loaded.LastWallpaper);
    }

    [Fact]
    public void Save_PurgesRecentsPointingToMissingFiles()
    {
        using var iso = new TestIsolation();
        var existing = iso.CreateTempMedia(".gif");
        var gone = Path.Combine(Settings.DirOverride!, "gone.mp4"); // jamais créé sur disque
        var s = new Settings { Recents = [existing, gone] };
        s.Save();

        var loaded = Settings.Load();
        Assert.Single(loaded.Recents);
        Assert.Equal(existing, loaded.Recents[0]);
    }
}
