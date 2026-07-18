using System.Windows;
using H.NotifyIcon;

namespace Wallflow;

public partial class App : Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _showSignal;
    private AppService? _service;
    private TaskbarIcon? _tray;
    private MainWindow? _window;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        _mutex = new Mutex(initiallyOwned: true, "Wallflow_SingleInstance", out var isFirst);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Wallflow_Show");
        if (!isFirst)
        {
            // Une instance tourne déjà : on lui demande de montrer sa fenêtre et on quitte.
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

        var mute = new System.Windows.Controls.MenuItem { Header = "Muet", IsCheckable = true };
        mute.Click += (_, _) =>
        {
            _service!.Settings.Muted = mute.IsChecked;
            _service.ApplyPlaybackSettings();
        };

        var vol25 = new System.Windows.Controls.MenuItem { Header = "25%" };
        vol25.Click += (_, _) => SetTrayVolume(25);
        var vol50 = new System.Windows.Controls.MenuItem { Header = "50%" };
        vol50.Click += (_, _) => SetTrayVolume(50);
        var vol75 = new System.Windows.Controls.MenuItem { Header = "75%" };
        vol75.Click += (_, _) => SetTrayVolume(75);
        var vol100 = new System.Windows.Controls.MenuItem { Header = "100%" };
        vol100.Click += (_, _) => SetTrayVolume(100);

        var volume = new System.Windows.Controls.MenuItem
        {
            Header = "Volume",
            Items = { mute, new System.Windows.Controls.Separator(), vol25, vol50, vol75, vol100 },
        };

        var quit = new System.Windows.Controls.MenuItem { Header = "Quitter" };
        quit.Click += (_, _) => Shutdown();

        void Sync()
        {
            pause.IsChecked = _service!.ManualPause;
            mute.IsChecked = _service.Settings.Muted;
            volume.Header = $"Volume : {_service.Settings.Volume}%";
        }
        _service!.StateChanged += () => Dispatcher.Invoke(Sync);
        Sync(); // état initial depuis settings.json, pas les défauts des MenuItem

        var tray = new TaskbarIcon
        {
            ToolTipText = "Wallflow",
            Icon = System.Drawing.SystemIcons.Application,
            ContextMenu = new System.Windows.Controls.ContextMenu { Items = { open, pause, volume, quit } },
        };
        tray.TrayLeftMouseUp += (_, _) => ShowWindow();
        tray.ForceCreate();
        return tray;
    }

    private void SetTrayVolume(int vol)
    {
        _service!.Settings.Volume = vol;
        _service.Settings.Muted = false;
        _service.ApplyPlaybackSettings();
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
