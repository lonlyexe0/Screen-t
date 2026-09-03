using System;
using System.IO;
using System.Linq;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace IkinciEkran;

public partial class App : System.Windows.Application
{
    private static readonly string LogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_debug.log");

    public static void Log(string msg)
    {
        try
        {
            File.AppendAllText(LogFile, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\r\n");
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        Log("App.OnStartup start");

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            Log($"AppDomain UnhandledException: {args.ExceptionObject}");
            MessageBox.Show($"Beklenmeyen Hata:\n{args.ExceptionObject}", "İkinci Ekran Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            Log($"DispatcherUnhandledException: {args.Exception}");
            MessageBox.Show($"Arayüz Hatası:\n{args.Exception.Message}", "İkinci Ekran Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);

        try
        {
            bool startMinimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);

            var mainWindow = new MainWindow();
            this.MainWindow = mainWindow;

            if (!startMinimized)
            {
                mainWindow.Show();
                mainWindow.Activate();
                mainWindow.Focus();
                Log("MainWindow shown successfully");
            }
            else
            {
                Log("Started minimized to tray");
            }
        }
        catch (Exception ex)
        {
            Log($"Startup error: {ex}");
            MessageBox.Show($"Uygulama başlatılırken hata:\n{ex.Message}", "Başlatma Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log($"App.OnExit called with code: {e.ApplicationExitCode}");
        base.OnExit(e);
    }
}
