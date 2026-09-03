using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using IkinciEkran.Services;
using IkinciEkran.Tray;
using Color = System.Windows.Media.Color;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;

namespace IkinciEkran;

public partial class MainWindow : Window
{
    private readonly VirtualDisplayController _displayController;
    private readonly ScreenCaptureEngine _captureEngine;
    private readonly StreamingServer _streamingServer;
    private readonly TrayIconManager _trayManager;

    private bool _isExplicitExit = false;

    public MainWindow()
    {
        App.Log("MainWindow constructor start");
        InitializeComponent();
        App.Log("InitializeComponent finished");

        _displayController = new VirtualDisplayController();
        _captureEngine = new ScreenCaptureEngine();
        _streamingServer = new StreamingServer();
        _trayManager = new TrayIconManager();
        App.Log("Services instantiated");

        SetupEvents();
        App.Log("SetupEvents finished");

        InitializeState();
        App.Log("InitializeState finished");
    }

    private void SetupEvents()
    {
        // Display controller
        _displayController.StatusChanged += isEnabled =>
        {
            Dispatcher.Invoke(() =>
            {
                TxtVirtualScreenStatus.Text = isEnabled ? "Etkin (Çalışıyor)" : "Devre Dışı";
                TxtVirtualScreenStatus.Foreground = new SolidColorBrush(isEnabled ? Color.FromRgb(16, 185, 129) : Color.FromRgb(248, 113, 113));
                BtnToggleDisplay.Content = isEnabled ? "🖥️ Sanal Ekranı Kapat" : "🖥️ Sanal Ekranı Aç";
                UpdateTrayState();
            });
        };

        // Streaming server
        _streamingServer.ClientCountChanged += count =>
        {
            Dispatcher.Invoke(() =>
            {
                TxtConnectedClients.Text = count > 0 ? $"{count} Cihaz Bağlı" : "0 Cihaz (Bekleniyor)";
                TxtConnectedClients.Foreground = new SolidColorBrush(count > 0 ? Color.FromRgb(16, 185, 129) : Color.FromRgb(56, 189, 248));
                UpdateTrayState();
            });
        };

        _streamingServer.LogMessage += msg =>
        {
            Dispatcher.Invoke(() =>
            {
                TxtLog.Text = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            });
        };

        // Connect capture engine to streaming server
        _captureEngine.OnFrameCaptured += bytes =>
        {
            _streamingServer.BroadcastFrame(bytes);
        };

        _captureEngine.OnLog += msg =>
        {
            Dispatcher.Invoke(() =>
            {
                TxtLog.Text = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            });
        };

        // Tray actions
        _trayManager.OnToggleWindowRequested += () =>
        {
            Dispatcher.Invoke(() =>
            {
                if (IsVisible)
                {
                    Hide();
                }
                else
                {
                    Show();
                    WindowState = WindowState.Normal;
                    Activate();
                }
            });
        };

        _trayManager.OnToggleStreamRequested += () => Dispatcher.Invoke(async () => await ToggleStreamAsync());
        _trayManager.OnToggleDisplayRequested += () => Dispatcher.Invoke(async () => await ToggleDisplayAsync());
        _trayManager.OnCopyIpRequested += () => Dispatcher.Invoke(() =>
        {
            Clipboard.SetText(TxtStreamUrl.Text);
            _trayManager.ShowBalloon("Panoya Kopyalandı", TxtStreamUrl.Text);
        });
        _trayManager.OnExitRequested += () => Dispatcher.Invoke(async () => await ExitApplicationAsync());

        // Intercept close button
        Closing += MainWindow_Closing;
    }

    private void InitializeState()
    {
        string localIp = StreamingServer.GetLocalIpAddress();
        string url = $"http://{localIp}:8080";
        TxtStreamUrl.Text = url;
        TxtLinuxCmd.Text = $"python3 client.py {url}";

        ChkRunOnStartup.IsChecked = SystemIntegration.IsRunOnStartup();

        // Auto-register to Start Menu / Windows Search immediately on first start
        SystemIntegration.RegisterStartMenuShortcut();

        // Initial check of virtual display
        _ = _displayController.CheckStatusAsync();
    }

    private void UpdateTrayState()
    {
        _trayManager.UpdateState(
            _streamingServer.IsRunning,
            _displayController.IsEnabled,
            _streamingServer.ConnectedClientsCount,
            TxtStreamUrl.Text
        );
    }

    private async Task ToggleStreamAsync()
    {
        if (!_streamingServer.IsRunning)
        {
            // START STREAMING
            BtnToggleStream.IsEnabled = false;
            TxtLog.Text = "Yayın ve Sanal Ekran başlatılıyor...";

            try
            {
                if (ChkAutoEnableDisplay.IsChecked == true && !_displayController.IsEnabled)
                {
                    await _displayController.EnableVirtualDisplayAsync();
                    await Task.Delay(1000); // Allow Windows display subsystem to detect new monitor
                }

                _streamingServer.Start(8080);
                _captureEngine.Start(60, 65L);

                IndicatorCircle.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                TxtStatus.Text = "YAYIN YAPILIYOR";
                TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));

                BtnToggleStream.Content = "⏹  YAYINI DURDUR";
                BtnToggleStream.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68));

                _trayManager.ShowBalloon("Yayın Başlatıldı", $"İkinci ekran yayını aktif!\n{TxtStreamUrl.Text}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Yayın başlatılırken hata oluştu:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                _streamingServer.Stop();
                _captureEngine.Stop();
            }
            finally
            {
                BtnToggleStream.IsEnabled = true;
                UpdateTrayState();
            }
        }
        else
        {
            // STOP STREAMING
            BtnToggleStream.IsEnabled = false;
            TxtLog.Text = "Yayın durduruluyor...";

            try
            {
                _captureEngine.Stop();
                _streamingServer.Stop();

                if (ChkAutoDisableDisplay.IsChecked == true && _displayController.IsEnabled)
                {
                    await _displayController.DisableVirtualDisplayAsync();
                }

                IndicatorCircle.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                TxtStatus.Text = "DURDURULDU";
                TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));

                BtnToggleStream.Content = "▶  YAYINI BAŞLAT";
                BtnToggleStream.Background = new SolidColorBrush(Color.FromRgb(2, 132, 199));

                _trayManager.ShowBalloon("Yayın Durduruldu", "Ekran yayını sonlandırıldı.");
            }
            finally
            {
                BtnToggleStream.IsEnabled = true;
                UpdateTrayState();
            }
        }
    }

    private async Task ToggleDisplayAsync()
    {
        BtnToggleDisplay.IsEnabled = false;
        try
        {
            if (_displayController.IsEnabled)
            {
                await _displayController.DisableVirtualDisplayAsync();
            }
            else
            {
                await _displayController.EnableVirtualDisplayAsync();
            }
        }
        finally
        {
            BtnToggleDisplay.IsEnabled = true;
            UpdateTrayState();
        }
    }

    private async void BtnToggleStream_Click(object sender, RoutedEventArgs e)
    {
        await ToggleStreamAsync();
    }

    private async void BtnToggleDisplay_Click(object sender, RoutedEventArgs e)
    {
        await ToggleDisplayAsync();
    }

    private void BtnCopyUrl_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(TxtStreamUrl.Text);
        BtnCopyUrl.Content = "✓ Kopyalandı!";
        Task.Delay(2000).ContinueWith(_ => Dispatcher.Invoke(() => BtnCopyUrl.Content = "📋 Kopyala"));
    }

    private void BtnOpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(TxtStreamUrl.Text) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Tarayıcı açılamadı: {ex.Message}");
        }
    }

    private void BtnCopyLinuxCmd_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(TxtLinuxCmd.Text);
        BtnCopyLinuxCmd.Content = "✓ Kopyalandı!";
        Task.Delay(2000).ContinueWith(_ => Dispatcher.Invoke(() => BtnCopyLinuxCmd.Content = "📋 Komutu Kopyala"));
    }

    private void BtnMinimizeToTray_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        _trayManager.ShowBalloon("İkinci Ekran Sistem Tepsisinde", "Uygulama arka planda çalışıyor. Saatin yanındaki simgeden dilediğiniz zaman erişebilirsiniz.");
    }

    private void BtnRegisterStartMenu_Click(object sender, RoutedEventArgs e)
    {
        bool ok = SystemIntegration.RegisterStartMenuShortcut();
        if (ok)
        {
            MessageBox.Show("Uygulama Windows Başlat ve Arama menüsüne başarıyla eklendi!\n\nArtık klavyeden Windows tuşuna basıp 'İkinci Ekran' yazdığınızda doğrudan açabilirsiniz.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Kısayol eklenemedi.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnRegisterDesktop_Click(object sender, RoutedEventArgs e)
    {
        bool ok = SystemIntegration.RegisterDesktopShortcut();
        if (ok)
        {
            MessageBox.Show("Masaüstünüze 'İkinci Ekran' kısayolu başarıyla eklendi!", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Kısayol eklenemedi.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ChkRunOnStartup_Click(object sender, RoutedEventArgs e)
    {
        bool isChecked = ChkRunOnStartup.IsChecked == true;
        SystemIntegration.SetRunOnStartup(isChecked);
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExplicitExit && ChkMinimizeToTrayOnClose.IsChecked == true)
        {
            e.Cancel = true;
            Hide();
            _trayManager.ShowBalloon("İkinci Ekran Arka Planda", "Yayın veya sanal ekran kesintiye uğramadan arka planda çalışmaya devam ediyor.\nTamamen çıkmak için tepsi menüsünden 'Çıkış'ı seçin.");
        }
    }

    private async void BtnExitApp_Click(object sender, RoutedEventArgs e)
    {
        await ExitApplicationAsync();
    }

    private async Task ExitApplicationAsync()
    {
        _isExplicitExit = true;
        TxtLog.Text = "Kapatılıyor ve sanal ekran temizleniyor...";

        _captureEngine.Stop();
        _streamingServer.Stop();

        if (ChkAutoDisableDisplay.IsChecked == true && _displayController.IsEnabled)
        {
            await _displayController.DisableVirtualDisplayAsync();
        }

        _trayManager.Dispose();
        Application.Current.Shutdown();
        Environment.Exit(0);
    }
}