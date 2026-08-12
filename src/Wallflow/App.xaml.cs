using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using H.NotifyIcon;

namespace Wallflow;

public partial class App : Application
{
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    // Palette Vermillon (docs/design/vermillon-tokens.md) — icône de marque dessinée en mémoire,
    // pas l'icône générique .NET extraite de l'exe.
    private static readonly Color VermillonAccent = ColorTranslator.FromHtml("#FF3B21");
    private static readonly Color VermillonCreme = ColorTranslator.FromHtml("#F4F2F0");
    private static readonly Color VermillonEncre = ColorTranslator.FromHtml("#0E0D12");
    private const int TrayIconSize = 32;
    private static readonly Bitmap BaseTrayBitmap = BuildBrandBitmap(TrayIconSize);

    // Pictogramme minimal (soleil + montagne) sur fond vermillon, coins carrés — lisible en petit
    // format, pas de texte (illisible à 16px), cohérent avec l'identité Vermillon de la fenêtre.
    private static Bitmap BuildBrandBitmap(int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.FillRectangle(new SolidBrush(VermillonAccent), 0, 0, size, size);

        var sun = new Rectangle(size * 5 / 8, size / 8, size / 4, size / 4);
        g.FillEllipse(Brushes.White, sun);

        System.Drawing.Point[] mountain =
        [
            new(size / 8, size * 7 / 8),
            new(size * 3 / 8, size * 3 / 8),
            new(size * 5 / 8, size * 5 / 8),
            new(size * 3 / 4, size * 3 / 8),
            new(size * 7 / 8, size * 7 / 8),
        ];
        g.FillPolygon(Brushes.White, mountain);
        return bmp;
    }

    private Mutex? _mutex;
    private EventWaitHandle? _showSignal;
    private AppService? _service;
    private TaskbarIcon? _tray;
    private MainWindow? _window;
    private bool? _lastTrayPaused;
    private string? _lastTrayTooltip;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        _mutex = new Mutex(initiallyOwned: true, "Wallflow_SingleInstance", out var isFirst);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Wallflow_Show");
        if (!isFirst)
        {
            // Une instance tourne déjà : on lui demande de montrer sa fenêtre et on quitte — sauf si
            // ce lancement vient de la tâche planifiée de relance au réveil (AppService.BuildWakeTaskArgs),
            // qui doit être silencieuse quand l'app tournait déjà (sinon la fenêtre s'ouvrirait à chaque
            // sortie de veille, y compris quand tout allait bien).
            if (!e.Args.Contains("--wake-relaunch"))
                _showSignal.Set();
            Shutdown();
            return;
        }
        ListenForShowSignal();

        _service = new AppService();
        _tray = BuildTray();

        if (!e.Args.Contains("--tray"))
            ShowWindow();
    }

    private TaskbarIcon BuildTray()
    {
        var open = new System.Windows.Controls.MenuItem { Header = "Ouvrir" };
        open.Click += (_, _) => ShowWindow();

        var pause = new System.Windows.Controls.MenuItem { Header = "Pause", IsCheckable = true };
        pause.Click += (_, _) => _service!.ManualPause = pause.IsChecked;

        var remove = new System.Windows.Controls.MenuItem { Header = "Retirer le fond d'écran" };
        remove.Click += (_, _) => _service!.RemoveWallpaper();

        var quit = new System.Windows.Controls.MenuItem { Header = "Quitter" };
        quit.Click += (_, _) => Shutdown();

        var tray = new TaskbarIcon
        {
            ContextMenu = new System.Windows.Controls.ContextMenu
            {
                Items = { open, pause, remove, new System.Windows.Controls.Separator(), quit },
            },
        };

        void Sync()
        {
            var paused = _service!.ManualPause;
            pause.IsChecked = paused;

            if (paused != _lastTrayPaused)
            {
                var (icon, hIcon) = BuildStateIcon(paused);
                tray.Icon = icon; // H.NotifyIcon dispose l'ancien Icon assigné (OnIconChanged)
                DestroyIcon(hIcon); // Icon.FromHandle ne possède pas hIcon (doc MS) — à libérer nous-mêmes
                _lastTrayPaused = paused;
            }

            var tooltip = _service.ActiveWallpaper is { } path ? Path.GetFileName(path) : "Wallflow";
            if (tooltip.Length > 127) tooltip = tooltip[..127]; // limite dure szTip (NOTIFYICONDATAW)
            if (tooltip != _lastTrayTooltip)
            {
                tray.ToolTipText = tooltip;
                _lastTrayTooltip = tooltip;
            }
        }
        tray.TrayLeftMouseUp += (_, _) => ShowWindow();
        // ForceCreate AVANT le premier Sync() : tant que IsCreated est faux, H.NotifyIcon se
        // contente de mémoriser le HICON assigné (pas de Shell_NotifyIcon) pour l'utiliser plus
        // tard à la création — le DestroyIcon immédiat de Sync() le détruisait avant cet usage
        // différé, laissant un slot de tray vide (HICON déjà invalide au moment du NIM_ADD).
        // efficiency mode désactivé : inutile pour une app always-on et suspecté d'empêcher
        // l'affichage de l'icône sur certaines versions de Windows 10.
        tray.ForceCreate(enablesEfficiencyMode: false);

        _service!.StateChanged += () => Dispatcher.Invoke(Sync);
        Sync(); // état initial depuis settings.json, pas les défauts des MenuItem

        return tray;
    }

    // Badge lecture/pause dessiné en mémoire par-dessus l'icône de marque (pas d'asset .ico à
    // maintenir). GetHicon() ne transfère pas la possession du handle à Icon.FromHandle (Remarks
    // MS Learn) : DestroyIcon explicite obligatoire côté appelant après assignation à tray.Icon,
    // sinon fuite GDI à chaque toggle (voir docs/research/tray-icon-state-tooltip.md §2).
    private static (Icon Icon, IntPtr HIcon) BuildStateIcon(bool paused)
    {
        const int size = TrayIconSize;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.DrawImage(BaseTrayBitmap, new Rectangle(0, 0, size, size));
            var badge = new Rectangle(size / 2, size / 2, size / 2, size / 2);
            g.FillEllipse(new SolidBrush(VermillonCreme), badge);
            var bar = Math.Max(1, badge.Width / 5);
            var glyph = new SolidBrush(VermillonEncre);
            if (paused)
            {
                g.FillRectangle(glyph, badge.X + bar, badge.Y + bar, bar, badge.Height - 2 * bar);
                g.FillRectangle(glyph, badge.Right - 2 * bar, badge.Y + bar, bar, badge.Height - 2 * bar);
            }
            else
            {
                System.Drawing.Point[] triangle =
                [
                    new(badge.X + bar, badge.Y + bar),
                    new(badge.X + bar, badge.Bottom - bar),
                    new(badge.Right - bar, badge.Y + badge.Height / 2),
                ];
                g.FillPolygon(glyph, triangle);
            }
        }
        var hIcon = bmp.GetHicon();
        return (Icon.FromHandle(hIcon), hIcon);
    }

    private void ShowWindow()
    {
        _window ??= new MainWindow(_service!);
        _window.Show();
        _window.Activate();
    }

    private void ListenForShowSignal()
    {
        var thread = new Thread(() =>
        {
            while (_showSignal!.WaitOne())
                Dispatcher.Invoke(ShowWindow);
        }) { IsBackground = true };
        thread.Start();
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        _tray?.Dispose();
        _service?.Shutdown();
        _mutex?.Dispose();
    }
}
