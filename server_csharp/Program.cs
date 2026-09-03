using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace ScreenServer;

public class DisplayInfo
{
    public uint OutputIndex { get; set; }
    public string DeviceName { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool IsPrimary { get; set; }
}

public class Program
{
    private static readonly ConcurrentDictionary<Guid, NetworkStream> _clients = new();
    private static volatile byte[]? _latestFrameBytes;
    private static readonly AutoResetEvent _newFrameEvent = new(false);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int nStdHandle);
    [DllImport("kernel32.dll")]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);
    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    private const int STD_INPUT_HANDLE = -10;
    private const uint ENABLE_QUICK_EDIT_MODE = 0x0040;
    private const uint ENABLE_EXTENDED_FLAGS = 0x0080;

    private static void DisableQuickEdit()
    {
        try
        {
            IntPtr handle = GetStdHandle(STD_INPUT_HANDLE);
            if (GetConsoleMode(handle, out uint mode))
            {
                mode &= ~ENABLE_QUICK_EDIT_MODE;
                mode |= ENABLE_EXTENDED_FLAGS;
                SetConsoleMode(handle, mode);
            }
        }
        catch { }
    }

    public static void Main(string[] args)
    {
        DisableQuickEdit();

        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            Console.WriteLine("\n[*] Kapatma istegi alindi. 2. Sanal ekran kapatiliyor...");
            DisableVirtualDisplay();
            Environment.Exit(0);
        };

        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {
            DisableVirtualDisplay();
        };

        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("=================================================");
        Console.WriteLine(" Yerel Ekran Yayini Sunucusu (HTTP Stream)       ");
        Console.WriteLine("=================================================");

        int port = args.Length > 0 && int.TryParse(args[0], out int p) ? p : 8080;
        int targetFps = 60;
        long targetFrameTimeMs = 1000 / targetFps;

        // Find primary local IP
        string localIp = GetLocalIpAddress();
        Console.WriteLine($"\n[INFO] Yayin bu makineden lokale acildi:");
        Console.WriteLine($" >>> http://{localIp}:{port} <<<");
        Console.WriteLine($"(Linux veya tarayicidan bu adrese girmeniz yeterlidir!)\n");

        // Start HTTP MJPEG Streaming Server
        var tcpListener = new TcpListener(IPAddress.Any, port);
        tcpListener.Start();
        Task.Run(() => AcceptClientsLoop(tcpListener));

        // Detect all available displays
        var displays = GetDisplays();
        Console.WriteLine("[INFO] Algilanan Monitorler:");
        DisplayInfo? targetDisplay = null;

        for (int i = 0; i < displays.Count; i++)
        {
            var d = displays[i];
            string typeStr = d.IsPrimary ? "Ana Ekran" : "IKINCI / SANAL EKRAN";
            Console.WriteLine($"  [{i}] {d.DeviceName} ({d.Width}x{d.Height} at {d.X},{d.Y}) -> {typeStr}");

            if (!d.IsPrimary && targetDisplay == null)
            {
                targetDisplay = d;
            }
        }

        if (targetDisplay == null && displays.Count > 0)
        {
            targetDisplay = displays[0];
        }

        if (targetDisplay == null)
        {
            Console.WriteLine("[HATA] Hicbir ekran algilanamadi!");
            return;
        }

        Console.WriteLine($"\n[INFO] Yayina Verilen Ekran: {targetDisplay.DeviceName} ({targetDisplay.Width}x{targetDisplay.Height})");

        // JPEG encoder settings: Quality 65 for smooth Wi-Fi
        var jpgEncoder = GetEncoder(ImageFormat.Jpeg);
        var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 65L);

        // Try initializing DXGI Desktop Duplication on selected output
        IScreenCapture captureEngine;
        try
        {
            Console.WriteLine($"[INFO] DXGI donanim yakalama deneniyor (Output #{targetDisplay.OutputIndex})...");
            captureEngine = new DxgiCapture(targetDisplay.OutputIndex);
            Console.WriteLine("[INFO] DXGI Donanim Yakalama Devrede!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] DXGI baslatilamadi ({ex.Message}).");
            Console.WriteLine($"[INFO] GDI BitBlt moduna gecildi: X={targetDisplay.X}, Y={targetDisplay.Y}");
            captureEngine = new GdiCapture(targetDisplay.X, targetDisplay.Y, targetDisplay.Width, targetDisplay.Height);
        }

        Console.WriteLine("[INFO] Ekran yakalama aktif. Istemciler baglandiginda yayin gidecek.");
        Console.WriteLine("[INFO] Durdurmak icin Ctrl+C basin.\n");

        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            long frameStart = stopwatch.ElapsedMilliseconds;

            using (var frameBmp = captureEngine.CaptureFrame())
            {
                if (frameBmp != null)
                {
                    DrawCursorOnBitmap(frameBmp, targetDisplay.X, targetDisplay.Y, targetDisplay.Width, targetDisplay.Height);

                    using var ms = new MemoryStream();
                    frameBmp.Save(ms, jpgEncoder, encoderParams);
                    _latestFrameBytes = ms.ToArray();
                    _newFrameEvent.Set();

                    // Push to all active HTTP clients
                    BroadcastFrame(_latestFrameBytes);
                }
            }

            long elapsed = stopwatch.ElapsedMilliseconds - frameStart;
            int sleepTime = (int)(targetFrameTimeMs - elapsed);
            if (sleepTime > 0)
            {
                Thread.Sleep(sleepTime);
            }
        }
    }

    private static void BroadcastFrame(byte[] jpegBytes)
    {
        if (_clients.IsEmpty) return;

        string frameHeader = $"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {jpegBytes.Length}\r\n\r\n";
        byte[] headerBytes = Encoding.ASCII.GetBytes(frameHeader);
        byte[] footerBytes = Encoding.ASCII.GetBytes("\r\n");

        foreach (var kvp in _clients)
        {
            var clientId = kvp.Key;
            var stream = kvp.Value;

            try
            {
                stream.Write(headerBytes, 0, headerBytes.Length);
                stream.Write(jpegBytes, 0, jpegBytes.Length);
                stream.Write(footerBytes, 0, footerBytes.Length);
            }
            catch
            {
                // Client disconnected
                _clients.TryRemove(clientId, out _);
                try { stream.Dispose(); } catch { }
                Console.WriteLine($"[INFO] Istemci ayrildi: {clientId}");
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private static void DrawCursorOnBitmap(Bitmap bmp, int monitorX, int monitorY, int monitorW, int monitorH)
    {
        if (!GetCursorPos(out POINT pt)) return;
        int curX = pt.x - monitorX;
        int curY = pt.y - monitorY;

        if (curX >= 0 && curX < monitorW && curY >= 0 && curY < monitorH)
        {
            using var g = Graphics.FromImage(bmp);
            Point[] arrow = new Point[]
            {
                new Point(curX, curY),
                new Point(curX, curY + 20),
                new Point(curX + 5, curY + 16),
                new Point(curX + 9, curY + 25),
                new Point(curX + 13, curY + 23),
                new Point(curX + 9, curY + 15),
                new Point(curX + 15, curY + 15)
            };
            g.FillPolygon(Brushes.White, arrow);
            using var pen = new Pen(Color.Black, 2f);
            g.DrawPolygon(pen, arrow);
        }
    }

    private static async Task AcceptClientsLoop(TcpListener listener)
    {
        while (true)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync();
                client.NoDelay = true; // Disable Nagle's algorithm for low latency!
                _ = Task.Run(() => HandleClientAsync(client));
            }
            catch (Exception)
            {
                break;
            }
        }
    }

    private static async Task HandleClientAsync(TcpClient client)
    {
        var clientId = Guid.NewGuid();
        var stream = client.GetStream();

        try
        {
            // Read HTTP request header
            byte[] buf = new byte[2048];
            int read = await stream.ReadAsync(buf, 0, buf.Length);
            string req = Encoding.ASCII.GetString(buf, 0, read);

            Console.WriteLine($"[INFO] Yeni Istemci Baglandi ({client.Client.RemoteEndPoint})!");

            // Send HTTP multipart response header
            string responseHeader =
                "HTTP/1.1 200 OK\r\n" +
                "Connection: close\r\n" +
                "Server: ScreenServer\r\n" +
                "Cache-Control: no-cache, no-store, must-revalidate, pre-check=0, post-check=0, max-age=0\r\n" +
                "Pragma: no-cache\r\n" +
                "Content-Type: multipart/x-mixed-replace; boundary=--frame\r\n" +
                "Access-Control-Allow-Origin: *\r\n\r\n";

            byte[] resHeaderBytes = Encoding.ASCII.GetBytes(responseHeader);
            await stream.WriteAsync(resHeaderBytes, 0, resHeaderBytes.Length);

            _clients.TryAdd(clientId, stream);
        }
        catch
        {
            client.Dispose();
        }
    }

    private static string GetLocalIpAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint ep)
            {
                return ep.Address.ToString();
            }
        }
        catch { }
        return "127.0.0.1";
    }

    private static List<DisplayInfo> GetDisplays()
    {
        var list = new List<DisplayInfo>();
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            if (factory.EnumAdapters1(0, out IDXGIAdapter1? adapter).Success && adapter != null)
            {
                for (uint i = 0; adapter.EnumOutputs(i, out IDXGIOutput? output).Success; i++)
                {
                    var desc = output!.Description;
                    int x = desc.DesktopCoordinates.Left;
                    int y = desc.DesktopCoordinates.Top;
                    int w = desc.DesktopCoordinates.Right - x;
                    int h = desc.DesktopCoordinates.Bottom - y;
                    bool isPrimary = (x == 0 && y == 0);

                    list.Add(new DisplayInfo
                    {
                        OutputIndex = i,
                        DeviceName = desc.DeviceName,
                        X = x,
                        Y = y,
                        Width = w,
                        Height = h,
                        IsPrimary = isPrimary
                    });
                    output.Dispose();
                }
                adapter.Dispose();
            }
        }
        catch { }

        if (list.Count == 0)
        {
            list.Add(new DisplayInfo
            {
                OutputIndex = 0,
                DeviceName = "\\\\.\\DISPLAY1",
                X = 0,
                Y = 0,
                Width = 1920,
                Height = 1080,
                IsPrimary = true
            });
        }

        return list;
    }

    private static ImageCodecInfo GetEncoder(ImageFormat format)
    {
        foreach (var codec in ImageCodecInfo.GetImageEncoders())
        {
            if (codec.FormatID == format.Guid)
                return codec;
        }
        throw new InvalidOperationException("JPEG encoder not found");
    }
    private static void DisableVirtualDisplay()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"Disable-PnpDevice -InstanceId 'ROOT\\DISPLAY\\0000' -Confirm:$false\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            Process.Start(psi)?.WaitForExit(5000);
            Console.WriteLine("[OK] 2. Sanal Ekran kapatildi!");
        }
        catch { }
    }

    private static void EnableVirtualDisplay()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"Enable-PnpDevice -InstanceId 'ROOT\\DISPLAY\\0000' -Confirm:$false\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            Process.Start(psi)?.WaitForExit(5000);
            Console.WriteLine("[OK] 2. Sanal Ekran etkinlestirildi!");
        }
        catch { }
    }
}

public interface IScreenCapture : IDisposable
{
    int Width { get; }
    int Height { get; }
    Bitmap? CaptureFrame();
}

public class DxgiCapture : IScreenCapture
{
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGIOutputDuplication _duplication;
    private readonly ID3D11Texture2D _stagingTexture;

    public int Width { get; }
    public int Height { get; }

    public DxgiCapture(uint outputIndex = 0)
    {
        D3D11.D3D11CreateDevice(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            new[] { FeatureLevel.Level_11_0 },
            out ID3D11Device? dev,
            out ID3D11DeviceContext? ctx).CheckError();

        _device = dev!;
        _context = ctx!;

        using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDevice.GetAdapter();
        adapter.EnumOutputs(outputIndex, out IDXGIOutput? output).CheckError();

        var desc = output!.Description;
        Width = desc.DesktopCoordinates.Right - desc.DesktopCoordinates.Left;
        Height = desc.DesktopCoordinates.Bottom - desc.DesktopCoordinates.Top;

        using var output1 = output.QueryInterface<IDXGIOutput1>();
        _duplication = output1.DuplicateOutput(_device);

        var stagingDesc = new Texture2DDescription
        {
            Width = (uint)Width,
            Height = (uint)Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None
        };
        _stagingTexture = _device.CreateTexture2D(stagingDesc);
        output.Dispose();
    }

    public Bitmap? CaptureFrame()
    {
        var res = _duplication.AcquireNextFrame(20, out _, out IDXGIResource? desktopResource);
        if (!res.Success || desktopResource == null)
            return null;

        using var texture = desktopResource.QueryInterface<ID3D11Texture2D>();
        _context.CopyResource(_stagingTexture, texture);
        _duplication.ReleaseFrame();

        var mapped = _context.Map(_stagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        var bmp = new Bitmap(Width, Height, (int)mapped.RowPitch, PixelFormat.Format32bppRgb, mapped.DataPointer);
        
        var resultBmp = new Bitmap(Width, Height, PixelFormat.Format32bppRgb);
        using (var g = Graphics.FromImage(resultBmp))
        {
            g.DrawImage(bmp, 0, 0);
        }

        bmp.Dispose();
        _context.Unmap(_stagingTexture, 0);
        return resultBmp;
    }

    public void Dispose()
    {
        _stagingTexture.Dispose();
        _duplication.Dispose();
        _context.Dispose();
        _device.Dispose();
    }
}

public class GdiCapture : IScreenCapture
{
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(out CURSORINFO pci);

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("user32.dll")]
    private static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyHeight, int istepIfAniCur, IntPtr hbrFlickerFreeDraw, int diFlags);

    private const int SRCCOPY = 0x00CC0020;
    private const int CURSOR_SHOWING = 0x00000001;
    private const int DI_NORMAL = 0x0003;

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("gdi32.dll")]
    private static extern bool GdiFlush();

    public GdiCapture(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public Bitmap? CaptureFrame()
    {
        IntPtr hScreenDC = GetDC(IntPtr.Zero);
        IntPtr hMemoryDC = CreateCompatibleDC(hScreenDC);
        IntPtr hBitmap = CreateCompatibleBitmap(hScreenDC, Width, Height);
        IntPtr hOldBitmap = SelectObject(hMemoryDC, hBitmap);

        BitBlt(hMemoryDC, 0, 0, Width, Height, hScreenDC, X, Y, SRCCOPY);
        GdiFlush();

        SelectObject(hMemoryDC, hOldBitmap);
        DeleteDC(hMemoryDC);
        ReleaseDC(IntPtr.Zero, hScreenDC);

        Bitmap bmp = Image.FromHbitmap(hBitmap);
        DeleteObject(hBitmap);

        // Canli Fare Imlecini Kesin Olarak Ciz
        if (GetCursorPos(out POINT curPos))
        {
            int curX = curPos.x - X;
            int curY = curPos.y - Y;

            if (curX >= 0 && curX < Width && curY >= 0 && curY < Height)
            {
                using var g = Graphics.FromImage(bmp);
                bool drawn = false;

                CURSORINFO ci = new CURSORINFO();
                ci.cbSize = Marshal.SizeOf(typeof(CURSORINFO));
                if (GetCursorInfo(out ci) && ci.hCursor != IntPtr.Zero)
                {
                    if (GetIconInfo(ci.hCursor, out ICONINFO ii))
                    {
                        // Sadece gercek renkli/seffaf imleclerde DrawIconEx kullan (Surukleme simgeleri vb.)
                        if (ii.hbmColor != IntPtr.Zero)
                        {
                            IntPtr hdc = g.GetHdc();
                            try
                            {
                                drawn = DrawIconEx(hdc, curX - ii.xHotspot, curY - ii.yHotspot, ci.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL);
                            }
                            finally
                            {
                                g.ReleaseHdc(hdc);
                            }
                        }
                        if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
                        if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
                    }
                }

                // Normal gezinirken (Monochrome imlec) kesinlikle gorunen net ok imleci ciz
                if (!drawn)
                {
                    Point[] arrow = new Point[]
                    {
                        new Point(curX, curY),
                        new Point(curX, curY + 19),
                        new Point(curX + 5, curY + 15),
                        new Point(curX + 9, curY + 23),
                        new Point(curX + 13, curY + 21),
                        new Point(curX + 9, curY + 14),
                        new Point(curX + 14, curY + 14)
                    };
                    g.FillPolygon(Brushes.White, arrow);
                    using var pen = new Pen(Color.Black, 2f);
                    g.DrawPolygon(pen, arrow);
                }
            }
        }

        return bmp;
    }

    public void Dispose() { }
}
