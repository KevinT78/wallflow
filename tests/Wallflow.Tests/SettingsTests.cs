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
        Assert.Equal("cover", s.VideoFit);
        Assert.True(s.Loop);
        Assert.Equal(1.0, s.Speed);
    }

    [Fact]
    public void RoundTrip()
    {
        var s = new Settings
        {
            VideoFit = "fill",
            Loop = false,
            Speed = 2.5,
            AutoStart = false,
            AutoPauseEnabled = false,
            SlideshowSnapshot = new(@"C:\Users\Me\Pictures\Slides", 600000, true),
        };
        var json = JsonSerializer.Serialize(s);
        var deserialized = JsonSerializer.Deserialize<Settings>(json)!;

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
    public void Load_IgnoresLegacyVolumeMutedFields_KeepsOtherFields()
    {
        using var iso = new TestIsolation();
        Directory.CreateDirectory(Settings.DirOverride!);
        File.WriteAllText(Path.Combine(Settings.DirOverride!, "settings.json"),
            """{"Volume":50,"Muted":true,"VideoFit":"fit","Speed":1.5}""");

        var loaded = Settings.Load();

        Assert.Equal("fit", loaded.VideoFit);
        Assert.Equal(1.5, loaded.Speed);
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
        var s = new Settings { Speed = 1.5, LastWallpaper = @"C:\x\y.gif" };
        s.Save();

        Assert.False(File.Exists(Path.Combine(Settings.DirOverride!, "settings.json.tmp")));
        var loaded = Settings.Load();
        Assert.Equal(1.5, loaded.Speed);
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
