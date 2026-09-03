using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace IkinciEkran.Tray;

public class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _menuTitle;
    private readonly ToolStripMenuItem _menuToggleWindow;
    private readonly ToolStripMenuItem _menuToggleStream;
    private readonly ToolStripMenuItem _menuToggleDisplay;
    private readonly ToolStripMenuItem _menuCopyIp;
    private readonly ToolStripMenuItem _menuExit;

    public event Action? OnToggleWindowRequested;
    public event Action? OnToggleStreamRequested;
    public event Action? OnToggleDisplayRequested;
    public event Action? OnCopyIpRequested;
    public event Action? OnExitRequested;

    public TrayIconManager()
    {
        _notifyIcon = new NotifyIcon();

        // Create a guaranteed native 32x32 / 16x16 HICON
        try
        {
            _notifyIcon.Icon = CreateNativeMonitorIcon();
        }
        catch
        {
            _notifyIcon.Icon = SystemIcons.Application;
        }

        _notifyIcon.Text = "İkinci Ekran - Durduruldu";
        _notifyIcon.Visible = true;

        // Context Menu
        var contextMenu = new ContextMenuStrip();

        _menuTitle = new ToolStripMenuItem("🖥️ İkinci Ekran") { Enabled = false, Font = new Font(FontFamily.GenericSansSerif, 9f, FontStyle.Bold) };
        _menuToggleWindow = new ToolStripMenuItem("🪟 Pencereyi Göster / Gizle");
        _menuToggleStream = new ToolStripMenuItem("▶️ Yayını Başlat");
        _menuToggleDisplay = new ToolStripMenuItem("🖥️ Sanal Ekranı Aç");
        _menuCopyIp = new ToolStripMenuItem("📋 Yayın Adresini Kopyala");
        _menuExit = new ToolStripMenuItem("❌ Tamamen Çıkış");

        _menuToggleWindow.Click += (s, e) => OnToggleWindowRequested?.Invoke();
        _menuToggleStream.Click += (s, e) => OnToggleStreamRequested?.Invoke();
        _menuToggleDisplay.Click += (s, e) => OnToggleDisplayRequested?.Invoke();
        _menuCopyIp.Click += (s, e) => OnCopyIpRequested?.Invoke();
        _menuExit.Click += (s, e) => OnExitRequested?.Invoke();

        contextMenu.Items.Add(_menuTitle);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(_menuToggleWindow);
        contextMenu.Items.Add(_menuToggleStream);
        contextMenu.Items.Add(_menuToggleDisplay);
        contextMenu.Items.Add(_menuCopyIp);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(_menuExit);

        _notifyIcon.ContextMenuStrip = contextMenu;

        // Tray Click Actions
        _notifyIcon.DoubleClick += (s, e) => OnToggleWindowRequested?.Invoke();
        _notifyIcon.Click += (s, e) =>
        {
            // If left click, toggle window
            if (e is MouseEventArgs me && me.Button == MouseButtons.Left)
            {
                OnToggleWindowRequested?.Invoke();
            }
        };
    }

    private static Icon CreateNativeMonitorIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Background circle dark slate
            using var brushBg = new SolidBrush(Color.FromArgb(15, 23, 42));
            g.FillEllipse(brushBg, 1, 1, 30, 30);

            // Glow border cyan
            using var penGlow = new Pen(Color.FromArgb(56, 189, 248), 2f);
            g.DrawEllipse(penGlow, 1, 1, 30, 30);

            // Monitor rect
            using var penMonitor = new Pen(Color.White, 1.5f);
            g.DrawRectangle(penMonitor, 6, 7, 20, 13);

            // Inner screen
            using var brushScreen = new SolidBrush(Color.FromArgb(14, 116, 144));
            g.FillRectangle(brushScreen, 8, 9, 16, 9);

            // Stand
            using var brushStand = new SolidBrush(Color.White);
            g.FillRectangle(brushStand, 14, 21, 4, 3);
            g.FillRectangle(brushStand, 11, 24, 10, 2);
        }

        IntPtr hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    public void UpdateState(bool isStreaming, bool isDisplayEnabled, int clientCount, string streamUrl)
    {
        _menuToggleStream.Text = isStreaming ? "⏹️ Yayını Durdur" : "▶️ Yayını Başlat";
        _menuToggleDisplay.Text = isDisplayEnabled ? "🖥️ Sanal Ekranı Kapat" : "🖥️ Sanal Ekranı Aç";

        string tip = isStreaming
            ? $"İkinci Ekran: Yayında ({clientCount} Cihaz) - {streamUrl}"
            : "İkinci Ekran: Durduruldu";

        if (tip.Length > 63) tip = tip.Substring(0, 60) + "...";
        _notifyIcon.Text = tip;
    }

    public void ShowBalloon(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon.ShowBalloonTip(3000, title, message, icon);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
