using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Wallflow;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly AppService _service;
    private bool _refreshing;

    public MainWindow(AppService service)
    {
        _service = service;
        InitializeComponent();

        VolumeSlider.ValueChanged += OnVolumeChanged;
        MuteToggle.Click += OnMuteToggle;
        FitCover.Checked += OnVideoFitChanged;
        FitFit.Checked += OnVideoFitChanged;
        FitFill.Checked += OnVideoFitChanged;
        SpeedSlider.ValueChanged += OnSpeedChanged;
        LoopToggle.Click += OnLoopToggle;

        _service.StateChanged += () => Dispatcher.Invoke(RefreshUi);
        RefreshUi();
    }

    private void RefreshUi()
    {
        _refreshing = true;

        PauseToggle.IsChecked = _service.ManualPause;
        AutoStartToggle.IsChecked = _service.Settings.AutoStart;

        VolumeSlider.Value = _service.Settings.Volume;
        MuteToggle.IsChecked = _service.Settings.Muted;
        switch (_service.Settings.VideoFit)
        {
            case "cover": FitCover.IsChecked = true; break;
            case "fit": FitFit.IsChecked = true; break;
            case "fill": FitFill.IsChecked = true; break;
        }
        SpeedSlider.Value = _service.Settings.Speed;
        LoopToggle.IsChecked = _service.Settings.Loop;

        _refreshing = false;

        RecentsList.Items.Clear();
        foreach (var path in _service.Recents)
        {
            var button = new Button
            {
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(4),
                ToolTip = path,
                Content = BuildRecentContent(path),
            };
            button.Click += (_, _) => TryApply(path);
            RecentsList.Items.Add(button);
        }
    }

    private static object BuildRecentContent(string path)
    {
        if (Thumbnail.For(path) is { } bitmap)
            return new Image { Source = bitmap, Width = 96, Height = 96, Stretch = Stretch.UniformToFill };
        return new TextBlock { Text = Path.GetFileName(path), Width = 96, TextTrimming = TextTrimming.CharacterEllipsis };
    }

    private void TryApply(string path)
    {
        var ok = _service.Apply(path);
        StatusText.Text = "Format non supporté ou fichier introuvable";
        StatusText.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            TryApply(files[0]);
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Médias|" + string.Join(";", AppService.SupportedExtensions.Select(x => "*" + x)),
        };
        if (dialog.ShowDialog() == true)
            TryApply(dialog.FileName);
    }

    private void OnVolumeChanged(object sender, RoutedEventArgs e)
    {
        if (_refreshing) return;
        _service.Settings.Volume = (int)VolumeSlider.Value;
        _service.Settings.Muted = MuteToggle.IsChecked == true;
        _service.ApplyPlaybackSettings();
    }

    private void OnMuteToggle(object sender, RoutedEventArgs e)
    {
        _service.Settings.Muted = MuteToggle.IsChecked == true;
        _service.ApplyPlaybackSettings();
    }

    private void OnVideoFitChanged(object sender, RoutedEventArgs e)
    {
        if (_refreshing) return;
        if (FitCover.IsChecked == true) _service.Settings.VideoFit = "cover";
        else if (FitFit.IsChecked == true) _service.Settings.VideoFit = "fit";
        else if (FitFill.IsChecked == true) _service.Settings.VideoFit = "fill";
        _service.ApplyPlaybackSettings();
    }

    private void OnSpeedChanged(object sender, RoutedEventArgs e)
    {
        if (_refreshing) return;
        _service.Settings.Speed = SpeedSlider.Value;
        _service.ApplyPlaybackSettings();
    }

    private void OnLoopToggle(object sender, RoutedEventArgs e)
    {
        _service.Settings.Loop = LoopToggle.IsChecked == true;
        _service.ApplyPlaybackSettings();
    }

    private void OnPauseToggle(object sender, RoutedEventArgs e) =>
        _service.ManualPause = PauseToggle.IsChecked == true;

    private void OnAutoStartToggle(object sender, RoutedEventArgs e) =>
        _service.SetAutoStart(AutoStartToggle.IsChecked == true);

    // Fermer = cacher : l'app vit dans le tray.
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
