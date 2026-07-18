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
        var pause = new System.Windows.Controls.MenuItem { Header = "Pause", IsCheckable = true };
        pause.Click += (_, _) => _service!.ManualPause = pause.IsChecked;
        _service!.StateChanged += () => Dispatcher.Invoke(() => pause.IsChecked = _service.ManualPause);

        var open = new System.Windows.Controls.MenuItem { Header = "Ouvrir" };
        open.Click += (_, _) => ShowWindow();

        var quit = new System.Windows.Controls.MenuItem { Header = "Quitter" };
        quit.Click += (_, _) => Shutdown();

        var tray = new TaskbarIcon
        {
            ToolTipText = "Wallflow",
            Icon = System.Drawing.SystemIcons.Application,
            ContextMenu = new System.Windows.Controls.ContextMenu { Items = { open, pause, quit } },
        };
        tray.TrayLeftMouseUp += (_, _) => ShowWindow();
        tray.ForceCreate();
        return tray;
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
