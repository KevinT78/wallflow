using Xunit;

namespace Wallflow.Tests;

public class WallpaperHostTests
{
    // Seul maillon du watchdog testable hors machine réelle : la recréation des players, elle,
    // demande un vrai WorkerW et un vrai mpv (vérifiée en live). Ce que ce test verrouille, c'est
    // qu'un host perdu est bien signalé comme mort — si HostsAlive renvoyait vrai par défaut, le
    // watchdog ne se déclencherait jamais et le bureau resterait noir sans un mot dans le journal.
    [Fact]
    public void HostsAlive_IsFalse_WhenAnyHostIsNotAWindow()
    {
        Assert.True(WallpaperHost.HostsAlive([]));                       // rien à surveiller ≠ cassé
        Assert.False(WallpaperHost.HostsAlive([IntPtr.Zero]));           // host jamais créé
        Assert.False(WallpaperHost.HostsAlive([new IntPtr(0xDEAD)]));    // handle détruit/invalide
    }
}
