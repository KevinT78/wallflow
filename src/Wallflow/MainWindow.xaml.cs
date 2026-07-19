using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBlock = System.Windows.Controls.TextBlock;
using Image = System.Windows.Controls.Image;

namespace Wallflow;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private const string AccentKey = "AccentFillColorDefaultBrush";

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
        Speed05.Checked += OnSpeedChanged;
        Speed10.Checked += OnSpeedChanged;
        Speed15.Checked += OnSpeedChanged;
        Speed20.Checked += OnSpeedChanged;
        LoopToggle.Click += OnLoopToggle;
        AutoStartToggle.Click += OnAutoStartToggle;

        _service.StateChanged += () => Dispatcher.Invoke(RefreshUi);
        RefreshUi();
    }

    private void RefreshUi()
    {
        _refreshing = true;

        PlayPauseIcon.Symbol = _service.ManualPause ? SymbolRegular.Play24 : SymbolRegular.Pause24;
        VolumeIcon.Symbol = _service.Settings.Muted ? SymbolRegular.SpeakerMute24 : SymbolRegular.Speaker224;

        VolumeSlider.Value = _service.Settings.Volume;
        VolumeReadout.Text = $"{_service.Settings.Volume}%";
        MuteToggle.IsChecked = _service.Settings.Muted;
        AutoStartToggle.IsChecked = _service.Settings.AutoStart;
        LoopToggle.IsChecked = _service.Settings.Loop;
        switch (_service.Settings.VideoFit)
        {
            case "cover": FitCover.IsChecked = true; break;
            case "fit": FitFit.IsChecked = true; break;
            case "fill": FitFill.IsChecked = true; break;
        }
        switch (SpeedPaliers.Nearest(_service.Settings.Speed))
        {
            case 0.5: Speed05.IsChecked = true; break;
            case 1.0: Speed10.IsChecked = true; break;
            case 1.5: Speed15.IsChecked = true; break;
            case 2.0: Speed20.IsChecked = true; break;
        }

        _refreshing = false;

        var recents = _service.Recents;
        RecentsList.Items.Clear();
        RecentsList.Items.Add(BuildPlusTile(empty: recents.Count == 0));
        foreach (var path in recents)
        {
            var isActive = string.Equals(path, _service.ActiveWallpaper, StringComparison.OrdinalIgnoreCase);
            RecentsList.Items.Add(BuildRecentTile(path, isActive));
        }
    }

    // Tuile « + » en tête de grille — même format que les vignettes ; résout aussi l'état vide.
    private Button BuildPlusTile(bool empty)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new SymbolIcon { Symbol = SymbolRegular.Add24, FontSize = 28, HorizontalAlignment = HorizontalAlignment.Center });
        if (empty)
            stack.Children.Add(new TextBlock
            {
                Text = "Dépose un fichier ou clique",
                Opacity = 0.7, FontSize = 12, Margin = new Thickness(0, 6, 0, 0),
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
            });

        var button = new Button
        {
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(3),
            ToolTip = "Parcourir",
            Content = new Grid { Width = 160, Height = 90, Children = { stack } },
        };
        button.Click += OnBrowse;
        return button;
    }

    private Button BuildRecentTile(string path, bool isActive)
    {
        var visual = new Grid { Width = 160, Height = 90, ClipToBounds = true };
        visual.Children.Add(BuildThumb(path));
        if (isActive)
        {
            var badge = new Border
            {
                Margin = new Thickness(4),
                Padding = new Thickness(6, 2, 6, 2),
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = "Actif", FontSize = 11, Foreground = Brushes.White },
            };
            badge.SetResourceReference(Border.BackgroundProperty, AccentKey);
            visual.Children.Add(badge);
        }

        var button = new Button
        {
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(3),
            ToolTip = path,
            Content = visual,
            BorderThickness = new Thickness(isActive ? 2 : 1),
        };
        if (isActive)
            button.SetResourceReference(Control.BorderBrushProperty, AccentKey);
        button.Click += (_, _) => TryApply(path);
        button.ContextMenu = BuildRecentMenu(path);
        return button;
    }

    private static FrameworkElement BuildThumb(string path)
    {
        if (Thumbnail.For(path) is { } bitmap)
            return new Image { Source = bitmap, Stretch = Stretch.UniformToFill };
        return new TextBlock
        {
            Text = Path.GetFileName(path),
            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center, Margin = new Thickness(4),
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
        };
    }

    private ContextMenu BuildRecentMenu(string path)
    {
        var remove = new MenuItem { Header = "Retirer des récents" };
        remove.Click += (_, _) => _service.RemoveFromRecents(path);
        var open = new MenuItem { Header = "Ouvrir l'emplacement du fichier" };
        open.Click += (_, _) => OpenLocation(path);
        return new ContextMenu { Items = { remove, open } };
    }

    private static void OpenLocation(string path)
    {
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    private void TryApply(string path)
    {
        if (!_service.Apply(path))
            ShowError("Format non supporté ou fichier introuvable");
    }

    private void ShowError(string message)
    {
        new Snackbar(SnackbarHost)
        {
            Title = "Impossible d'appliquer",
            Content = message,
            Appearance = ControlAppearance.Danger,
            Timeout = TimeSpan.FromSeconds(4),
        }.Show();
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            TryApply(files[0]);
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        var ok = e.Data.GetDataPresent(DataFormats.FileDrop);
        DropOverlay.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) =>
        DropOverlay.Visibility = Visibility.Collapsed;

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Médias|" + string.Join(";", AppService.SupportedExtensions.Select(x => "*" + x)),
        };
        if (dialog.ShowDialog() == true)
            TryApply(dialog.FileName);
    }

    private void OnVolumeButtonClick(object sender, RoutedEventArgs e) => VolumeFlyout.Show();
    private void OnSettingsButtonClick(object sender, RoutedEventArgs e) => SettingsFlyout.Show();

    private void OnVolumeChanged(object sender, RoutedEventArgs e)
    {
        VolumeReadout.Text = $"{(int)VolumeSlider.Value}%";
        if (_refreshing) return;
        _service.Settings.Volume = (int)VolumeSlider.Value;
        _service.Settings.Muted = MuteToggle.IsChecked == true;
        _service.ApplyPlaybackSettings();
    }

    private void OnMuteToggle(object sender, RoutedEventArgs e)
    {
        if (_refreshing) return;
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
        _service.Settings.Speed = sender switch
        {
            _ when sender == Speed05 => 0.5,
            _ when sender == Speed10 => 1.0,
            _ when sender == Speed15 => 1.5,
            _ => 2.0,
        };
        _service.ApplyPlaybackSettings();
    }

    private void OnLoopToggle(object sender, RoutedEventArgs e)
    {
        if (_refreshing) return;
        _service.Settings.Loop = LoopToggle.IsChecked == true;
        _service.ApplyPlaybackSettings();
    }

    private void OnPauseToggle(object sender, RoutedEventArgs e) =>
        _service.ManualPause = !_service.ManualPause;

    private void OnAutoStartToggle(object sender, RoutedEventArgs e) =>
        _service.SetAutoStart(AutoStartToggle.IsChecked == true);

    private void OnRemoveWallpaper(object sender, RoutedEventArgs e) =>
        _service.RemoveWallpaper();

    private void OnQuit(object sender, RoutedEventArgs e) =>
        Application.Current.Shutdown();

    // Fermer = cacher : l'app vit dans le tray.
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
