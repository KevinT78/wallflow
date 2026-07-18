using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Wallflow;

/// <summary>
/// Tout le WinAPI WorkerW vit ici : fait apparaître la fenêtre du shell située derrière
/// les icônes du bureau, et y crée un HWND hôte par écran (dans lequel mpv rendra via wid).
/// Technique non documentée par Microsoft — référence : Lively Wallpaper.
/// </summary>
public static class WallpaperHost
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string? windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, string? vparam, uint winIni);

    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;

    private const uint SPI_SETDESKWALLPAPER = 0x0014;
    private const uint SPIF_UPDATEINIFILE = 0x01;
    private const uint SPIF_SENDCHANGE = 0x02;

    private static IntPtr _workerW;

    /// <summary>Provoque la création du WorkerW par le shell et le localise. À appeler une fois au démarrage.</summary>
    public static void Init()
    {
        var progman = FindWindow("Progman", null);
        // Message non documenté 0x052C : demande à Progman de créer le WorkerW derrière les icônes.
        SendMessageTimeout(progman, 0x052C, 0xD, 0x1, 0, 1000, out _);

        _workerW = IntPtr.Zero;
        EnumWindows((top, _) =>
        {
            // Le WorkerW cible est le frère qui SUIT la fenêtre contenant SHELLDLL_DefView (les icônes).
            if (FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                _workerW = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
            return true;
        }, IntPtr.Zero);

        // Win11 24H2 : plus de WorkerW séparé, Progman héberge directement le fond.
        if (_workerW == IntPtr.Zero)
            _workerW = progman;
    }

    /// <summary>Crée une fenêtre enfant du WorkerW couvrant l'écran donné. Coordonnées relatives à l'écran virtuel.</summary>
    public static IntPtr CreateHostFor(Screen screen)
    {
        var vs = SystemInformation.VirtualScreen;
        var b = screen.Bounds;
        var hwnd = CreateWindowEx(0, "Static", null, WS_CHILD | WS_VISIBLE,
            b.X - vs.X, b.Y - vs.Y, b.Width, b.Height, _workerW, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"CreateWindowEx a échoué (Win32 {Marshal.GetLastWin32Error()})");
        return hwnd;
    }

    public static void DestroyHost(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero) DestroyWindow(hwnd);
    }

    /// <summary>
    /// Repeint le fond natif Windows enregistré. Sans ça, détruire les fenêtres hôtes laisse
    /// le bureau blanc : le fond natif ne réapparaît pas tout seul.
    /// </summary>
    public static void RestoreDesktop() =>
        SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, null, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
}
