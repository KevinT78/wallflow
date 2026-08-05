using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

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
        var hwnd = TryCreateHost(screen);
        // Le shell peut recréer le WorkerW (ex. coupe du diaporama via IDesktopWallpaper) : le handle
        // capturé par Init() est alors périmé et CreateWindowEx échoue (Win32 1400). On re-localise le
        // WorkerW courant puis on retente une fois avant d'abandonner.
        if (hwnd == IntPtr.Zero)
        {
            Init();
            hwnd = TryCreateHost(screen);
        }
        if (hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"CreateWindowEx a échoué (Win32 {Marshal.GetLastWin32Error()})");
        return hwnd;
    }

    private static IntPtr TryCreateHost(Screen screen)
    {
        var vs = SystemInformation.VirtualScreen;
        var b = screen.Bounds;
        return CreateWindowEx(0, "Static", null, WS_CHILD | WS_VISIBLE,
            b.X - vs.X, b.Y - vs.Y, b.Width, b.Height, _workerW, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
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

    // ----- Diaporama Windows natif via IDesktopWallpaper (COM, dispo depuis Windows 8) -----
    // Couche distincte du WorkerW/RestoreDesktop ci-dessus : ici on touche au RÉGLAGE natif du
    // fond (diaporama on/off), pas au repaint. Non testable unitairement (COM système) — vérifié
    // en live. Best-effort : toute erreur COM est avalée pour ne jamais casser l'Apply du wallpaper.

    /// <summary>
    /// Si le fond Windows est en mode diaporama, capture sa config (dossier + intervalle +
    /// mélange) puis coupe le défilement via Enable(false). Retourne la config, ou null si
    /// aucun diaporama actif (no-op). Mécanisme validé en live sur Win10 (voir Note ci-dessous).
    /// </summary>
    // NOTE (vérifié live 2026-07-19, Win10 19045) : l'approche « SetWallpaper avec l'image
    // courante » du PRD NE coupe PAS le diaporama, et GetStatus n'est pas fiable en
    // programmatique (reste DSS_ENABLED même diaporama posé). Signaux fiables retenus :
    // registre BackgroundType==2 pour détecter, Enable(false)/Enable(true) pour couper/relancer.
    public static SlideshowSnapshot? PauseSlideshowIfActive()
    {
        if (!IsSlideshowActive()) return null;

        IDesktopWallpaper dw;
        try { dw = CreateDesktopWallpaper(); }
        catch (Exception ex) { Log.Warn($"PauseSlideshowIfActive : impossible de créer IDesktopWallpaper ({ex.Message})"); return null; }

        // Capture best-effort : dossier/intervalle servent à restaurer plus tard. Son échec ne doit
        // PAS empêcher de couper le défilement — le vrai but est de stopper le flicker, pas de capturer.
        SlideshowSnapshot? snapshot = null;
        try
        {
            dw.GetSlideshow(out var items);
            items.GetItemAt(0, out var item);
            item.GetDisplayName(SIGDN_FILESYSPATH, out var folder);
            dw.GetSlideshowOptions(out var options, out var tick);
            snapshot = new SlideshowSnapshot(folder, tick, (options & DESKTOP_SLIDESHOW_OPTIONS.DSO_SHUFFLEIMAGES) != 0);
        }
        catch (Exception ex) { Log.Warn($"PauseSlideshowIfActive : capture ratée ({ex.Message})"); /* capture ratée : on coupe quand même le défilement ci-dessous */ }

        try { dw.Enable(false); } // coupe le défilement (status → 0, plus de tick) — toujours tenté
        catch (Exception ex) { Log.Warn($"PauseSlideshowIfActive : coupure du défilement échouée ({ex.Message})"); /* best-effort : ni capture ni coupure plutôt qu'un Apply cassé */ }

        return snapshot;
    }

    /// <summary>Relance le diaporama Windows à l'identique depuis une config capturée.</summary>
    public static void ResumeSlideshow(SlideshowSnapshot snapshot)
    {
        try
        {
            var dw = CreateDesktopWallpaper();
            var iidItem = typeof(IShellItem).GUID;
            SHCreateItemFromParsingName(snapshot.FolderPath, IntPtr.Zero, ref iidItem, out var shellItem);
            var iidArray = typeof(IShellItemArray).GUID;
            SHCreateShellItemArrayFromShellItem(shellItem, ref iidArray, out var array);
            dw.SetSlideshow(array);
            var options = snapshot.Shuffle ? DESKTOP_SLIDESHOW_OPTIONS.DSO_SHUFFLEIMAGES : 0;
            dw.SetSlideshowOptions(options, snapshot.IntervalMs);
            dw.Enable(true); // réactive le défilement coupé par PauseSlideshowIfActive
        }
        catch (Exception ex) { Log.Warn($"ResumeSlideshow échoué pour {snapshot.FolderPath} ({ex.Message})"); /* best-effort : le diaporama ne reprend pas, mais l'app ne crashe pas */ }
    }

    /// <summary>BackgroundType : 0=image, 1=couleur, 2=diaporama. Seul signal fiable du mode courant.</summary>
    private static bool IsSlideshowActive() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers",
            "BackgroundType", 0) is int t && t == 2;

    private static IDesktopWallpaper CreateDesktopWallpaper()
    {
        var type = Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD"))
            ?? throw new InvalidOperationException("CLSID_DesktopWallpaper introuvable");
        return (IDesktopWallpaper)Activator.CreateInstance(type)!;
    }

    private const uint SIGDN_FILESYSPATH = 0x80058000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    [DllImport("shell32.dll", PreserveSig = false)]
    private static extern void SHCreateShellItemArrayFromShellItem(
        IShellItem psi, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemArray ppv);

    [Flags]
    private enum DESKTOP_SLIDESHOW_STATE { DSS_ENABLED = 0x1, DSS_SLIDESHOW = 0x2, DSS_DISABLED_BY_REMOTE_SESSION = 0x4 }

    [Flags]
    private enum DESKTOP_SLIDESHOW_OPTIONS { DSO_SHUFFLEIMAGES = 0x1 }

    private enum DESKTOP_SLIDESHOW_DIRECTION { DSD_FORWARD = 0, DSD_BACKWARD = 1 }

    private enum DESKTOP_WALLPAPER_POSITION { Center = 0, Tile = 1, Stretch = 2, Fit = 3, Fill = 4, Span = 5 }

    // Vtable IDesktopWallpaper au complet et DANS L'ORDRE : les slots non utilisés doivent
    // exister pour que ceux qu'on appelle tombent au bon offset. Params simplifiés en IntPtr
    // pour les méthodes jamais appelées.
    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        void GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID, [MarshalAs(UnmanagedType.LPWStr)] out string wallpaper);
        void GetMonitorDevicePathAt(uint monitorIndex, [MarshalAs(UnmanagedType.LPWStr)] out string monitorID);
        void GetMonitorDevicePathCount(out uint count);
        void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID, out RECT displayRect);
        void SetBackgroundColor(uint color);
        void GetBackgroundColor(out uint color);
        void SetPosition(DESKTOP_WALLPAPER_POSITION position);
        void GetPosition(out DESKTOP_WALLPAPER_POSITION position);
        void SetSlideshow(IShellItemArray items);
        void GetSlideshow(out IShellItemArray items);
        void SetSlideshowOptions(DESKTOP_SLIDESHOW_OPTIONS options, uint slideshowTick);
        void GetSlideshowOptions(out DESKTOP_SLIDESHOW_OPTIONS options, out uint slideshowTick);
        void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID, DESKTOP_SLIDESHOW_DIRECTION direction);
        void GetStatus(out DESKTOP_SLIDESHOW_STATE state);
        void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppvOut);
        void GetPropertyStore(int flags, ref Guid riid, out IntPtr ppv);
        void GetPropertyDescriptionList(IntPtr keyType, ref Guid riid, out IntPtr ppv);
        void GetAttributes(int dwAttribFlags, uint sfgaoMask, out uint psfgaoAttribs);
        void GetCount(out uint pdwNumItems);
        void GetItemAt(uint dwIndex, out IShellItem ppsi);
        void EnumItems(out IntPtr ppenumShellItems);
    }
}
