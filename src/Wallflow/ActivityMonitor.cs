using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Wallflow;

/// <summary>
/// Pause auto : app au premier plan en plein écran, PC sur batterie, ou économie d'énergie.
/// Détection immédiate par hook WinEvent (changement de premier plan) + SystemEvents (puissance),
/// avec le timer 2 s en filet de sécurité si un événement était raté. Émet ShouldPauseChanged
/// uniquement quand l'état bascule (le Poll est idempotent).
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

    [DllImport("powrprof.dll")]
    private static extern int PowerGetActiveOverlayScheme(out Guid activeOverlayScheme);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmod,
        WinEventDelegate proc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    private const uint EventSystemForeground = 0x0003;
    private const uint WineventOutOfContext = 0x0000;

    /// <summary>GUID_BATTERY_SAVER_OVERLAY_SCHEME : « économiseur de batterie » (Win10) / « efficacité
    /// énergétique » (Win11) — actif aussi bien sur batterie que sur secteur pour l'Energy Saver.</summary>
    private static readonly Guid BatterySaverOverlay = new("961cc777-2547-4f9d-8174-7d86181b8a7a");

    public event Action<bool>? ShouldPauseChanged;

    /// <summary>Coupe hook WinEvent + SystemEvents + timer. Isolation des tests uniquement :
    /// sans ça, chaque AppService de test pose un hook système global jamais décroché.</summary>
    public static bool Disabled { get; set; }

    private bool _lastShouldPause;
    private IntPtr _foregroundHook;
    private readonly WinEventDelegate _hookProc = (_, _, _, _, _, _, _) => { };

    public ActivityMonitor()
    {
        if (Disabled) return;

        // Filet de sécurité : rattrape tout état que les événements ci-dessous auraient raté.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => Poll();
        timer.Start();

        // Détection immédiate du plein écran : le hook est livré sur la thread qui l'a posé
        // (celle du Dispatcher, qui pompe les messages) — le callback tourne donc sur le thread UI.
        _hookProc = (_, _, _, _, _, _, _) => Poll();
        _foregroundHook = SetWinEventHook(EventSystemForeground, EventSystemForeground,
            IntPtr.Zero, _hookProc, 0, 0, WineventOutOfContext);
        if (_foregroundHook == IntPtr.Zero)
            Log.Warn("SetWinEventHook(EVENT_SYSTEM_FOREGROUND) a échoué — détection plein écran réduite au polling");

        // Changement de source d'alimentation / sortie de veille → réévaluer immédiatement.
        SystemEvents.PowerModeChanged += (_, _) => Poll();
    }

    /// <summary>Relâche le hook. L'app ne le dispose jamais en pratique (process lifetime),
    /// mais les tests et un éventuel futur cycle de vie propre peuvent s'en servir.</summary>
    public void Dispose()
    {
        if (_foregroundHook != IntPtr.Zero)
        {
            UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }
    }

    private void Poll()
    {
        // Garde-fou : Poll est appelé depuis un callback WinEvent, le timer et SystemEvents — une
        // exception ici est fatale (thread système → Dispatcher → processus). On ne doit jamais
        // laisser une détection best-effort tuer l'app (ex. EntryPointNotFoundException de
        // PowerGetActiveOverlayScheme sous Windows 10, cf. crash 2026-08-03).
        try
        {
            var shouldPause = OnBattery() || EnergySaverActive() || FullscreenAppActive();
            if (shouldPause == _lastShouldPause) return;
            _lastShouldPause = shouldPause;
            ShouldPauseChanged?.Invoke(shouldPause);
        }
        catch (Exception ex)
        {
            Log.Warn($"ActivityMonitor.Poll a échoué ({ex.GetType().Name} : {ex.Message})");
        }
    }

    private static bool OnBattery() =>
        SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;

    private static bool EnergySaverActive()
    {
        // PowerGetActiveOverlayScheme n'existe qu'à partir de Windows 11 (build ≥ 22000). Sous
        // Windows 10 l'appel lève EntryPointNotFoundException à la première notification d'alimentation
        // — détecté une fois, puis court-circuité. Dégradation assumée : sur Win10, seule la pause
        // batterie (PowerLineStatus) reste active ; l'économie d'énergie est détectée sur Win11.
        if (_energySaverUnsupported) return false;
        try
        {
            return PowerGetActiveOverlayScheme(out var scheme) == 0 && scheme == BatterySaverOverlay;
        }
        catch (EntryPointNotFoundException)
        {
            _energySaverUnsupported = true;
            return false;
        }
        catch (Exception)
        {
            return false; // best-effort : échec de la requête → pas d'économie d'énergie détectée
        }
    }

    private static bool _energySaverUnsupported;

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
