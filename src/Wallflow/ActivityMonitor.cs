using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Windows.Threading;

namespace Wallflow;

/// <summary>
/// Surveille toutes les 2 s : app au premier plan en plein écran, ou PC sur batterie.
/// Émet ShouldPauseChanged uniquement quand l'état bascule.
/// ponytail: polling 2 s — hooks WinEvent si le poll se voit un jour en consommation.
/// </summary>
public sealed class ActivityMonitor
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder buffer, int maxCount);

    public event Action<bool>? ShouldPauseChanged;

    private bool _lastShouldPause;

    public ActivityMonitor()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => Poll();
        timer.Start();
    }

    private void Poll()
    {
        var shouldPause = OnBattery() || FullscreenAppActive();
        if (shouldPause == _lastShouldPause) return;
        _lastShouldPause = shouldPause;
        ShouldPauseChanged?.Invoke(shouldPause);
    }

    private static bool OnBattery() =>
        SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;

    private static bool FullscreenAppActive()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;

        var sb = new StringBuilder(64);
        GetClassName(fg, sb, sb.Capacity);
        // Le bureau lui-même couvre l'écran — ne pas se mettre en pause à cause de soi ou du shell.
        if (sb.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;

        if (!GetWindowRect(fg, out var r)) return false;
        var b = Screen.FromHandle(fg).Bounds;
        return r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
    }
}
