using System.IO;
using Xunit;

namespace Wallflow.Tests;

public class WallpaperCacheTests
{
    [Fact]
    public void PruneOrphans_DeletesMp4NotMatchingAnyActivePath()
    {
        using var iso = new TestIsolation();
        WallpaperCache.Disabled = false;
        var active = iso.CreateTempMedia(".gif");
        var evicted = iso.CreateTempMedia(".gif");
        Directory.CreateDirectory(WallpaperCache.DirOverride!);
        var activeCache = WallpaperCache.CachePathFor(active);
        var orphanCache = WallpaperCache.CachePathFor(evicted);
        File.WriteAllBytes(activeCache, []);
        File.WriteAllBytes(orphanCache, []);

        WallpaperCache.PruneOrphans([active]);

        Assert.True(File.Exists(activeCache));
        Assert.False(File.Exists(orphanCache));
    }

    [Fact]
    public void PruneOrphans_IgnoresInFlightConversions()
    {
        using var iso = new TestIsolation();
        WallpaperCache.Disabled = false;
        Directory.CreateDirectory(WallpaperCache.DirOverride!);
        var tmp = Path.Combine(WallpaperCache.DirOverride!, Guid.NewGuid() + ".tmp.mp4");
        File.WriteAllBytes(tmp, []);

        WallpaperCache.PruneOrphans([]);

        Assert.True(File.Exists(tmp)); // pas encore promue en .mp4 final : pas un orphelin
    }

    [Fact]
    public void PruneOrphans_WhenDisabled_DoesNothing()
    {
        using var iso = new TestIsolation(); // Disabled = true par défaut
        Directory.CreateDirectory(WallpaperCache.DirOverride!);
        var orphan = Path.Combine(WallpaperCache.DirOverride!, "orphan.mp4");
        File.WriteAllBytes(orphan, []);

        WallpaperCache.PruneOrphans([]);

        Assert.True(File.Exists(orphan));
    }

    [Fact]
    public void PruneOrphans_StaleSourceKey_DeletesOldCacheForStillRecentPath()
    {
        // Fichier source modifié depuis la conversion (issue #22) : l'ancienne clé (chemin+taille+date)
        // ne correspond plus à celle recalculée pour le même chemin, donc l'ancien .mp4 est orphelin.
        using var iso = new TestIsolation();
        WallpaperCache.Disabled = false;
        var path = iso.CreateTempMedia(".gif");
        Directory.CreateDirectory(WallpaperCache.DirOverride!);
        var staleCache = Path.Combine(WallpaperCache.DirOverride!, "stale-key.mp4");
        File.WriteAllBytes(staleCache, []); // simule une conversion pour une version antérieure du fichier

        WallpaperCache.PruneOrphans([path]); // même chemin, mais la clé actuelle ne matche pas "stale-key"

        Assert.False(File.Exists(staleCache));
    }
}
