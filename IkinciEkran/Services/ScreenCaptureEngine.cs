using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace IkinciEkran.Services;

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

public interface IScreenCapture : IDisposable
{
    int Width { get; }
    int Height { get; }
    Bitmap? CaptureFrame();
}

public class ScreenCaptureEngine : IDisposable
{
    public event Action<byte[]>? OnFrameCaptured;
    public event Action<string>? OnLog;

    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private IScreenCapture? _captureEngine;
    private volatile bool _isRunning = false;

    public bool IsRunning => _isRunning;
    public DisplayInfo? ActiveDisplay { get; private set; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    public static List<DisplayInfo> GetDisplays()
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

    public void Start(int targetFps = 60, long quality = 65L)
    {
        if (_isRunning) return;

        var displays = GetDisplays();
        DisplayInfo? target = null;

        // Prefer secondary / virtual display
        for (int i = 0; i < displays.Count; i++)
        {
            if (!displays[i].IsPrimary && target == null)
            {
                target = displays[i];
            }
        }

        if (target == null && displays.Count > 0)
        {
            target = displays[0];
        }

        if (target == null)
        {
            OnLog?.Invoke("Hata: Hiçbir ekran algılanamadı!");
            return;
        }

        ActiveDisplay = target;
        OnLog?.Invoke($"Ekran Seçildi: {target.DeviceName} ({target.Width}x{target.Height})");

        try
        {
            _captureEngine = new DxgiCapture(target.OutputIndex);
            OnLog?.Invoke("DXGI Donanım Hızlandırmalı Yakalama Devrede.");
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"DXGI başlatılamadı ({ex.Message}). GDI Moduna geçiliyor.");
            _captureEngine = new GdiCapture(target.X, target.Y, target.Width, target.Height);
        }

        _cts = new CancellationTokenSource();
        _isRunning = true;
        var token = _cts.Token;

        _captureTask = Task.Run(() => CaptureLoop(target, targetFps, quality, token), token);
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;
        _cts?.Cancel();

        try
        {
            _captureTask?.Wait(1000);
        }
        catch { }

        _captureEngine?.Dispose();
        _captureEngine = null;
        _cts?.Dispose();
        _cts = null;
    }

    private void CaptureLoop(DisplayInfo targetDisplay, int targetFps, long quality, CancellationToken token)
    {
        long targetFrameTimeMs = 1000 / targetFps;
        var jpgEncoder = GetEncoder(ImageFormat.Jpeg);
        var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);

        var stopwatch = Stopwatch.StartNew();

        while (!token.IsCancellationRequested && _isRunning)
        {
            long frameStart = stopwatch.ElapsedMilliseconds;

            try
            {
                using var frameBmp = _captureEngine?.CaptureFrame();
                if (frameBmp != null)
                {
                    DrawCursorOnBitmap(frameBmp, targetDisplay.X, targetDisplay.Y, targetDisplay.Width, targetDisplay.Height);

                    using var ms = new MemoryStream();
                    frameBmp.Save(ms, jpgEncoder, encoderParams);
                    byte[] jpegBytes = ms.ToArray();
                    OnFrameCaptured?.Invoke(jpegBytes);
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"Kare hatası: {ex.Message}");
                Thread.Sleep(50);
            }

            long elapsed = stopwatch.ElapsedMilliseconds - frameStart;
            int sleepTime = (int)(targetFrameTimeMs - elapsed);
            if (sleepTime > 0)
            {
                Thread.Sleep(sleepTime);
            }
        }
    }

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

    private static ImageCodecInfo GetEncoder(ImageFormat format)
    {
        foreach (var codec in ImageCodecInfo.GetImageEncoders())
        {
            if (codec.FormatID == format.Guid)
                return codec;
        }
        throw new InvalidOperationException("JPEG encoder not found");
    }

    public void Dispose()
    {
        Stop();
    }
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
        var result = _duplication.AcquireNextFrame(20, out OutduplFrameInfo frameInfo, out IDXGIResource? desktopResource);
        if (result.Failure || desktopResource == null)
        {
            return null;
        }

        try
        {
            using var desktopTexture = desktopResource.QueryInterface<ID3D11Texture2D>();
            _context.CopyResource(_stagingTexture, desktopTexture);

            var mapped = _context.Map(_stagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppRgb);
                var boundsRect = new Rectangle(0, 0, Width, Height);
                var bmpData = bmp.LockBits(boundsRect, ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);

                unsafe
                {
                    byte* srcPtr = (byte*)mapped.DataPointer.ToPointer();
                    byte* dstPtr = (byte*)bmpData.Scan0.ToPointer();
                    int srcStride = (int)mapped.RowPitch;
                    int dstStride = bmpData.Stride;
                    int bytesPerLine = Math.Min(srcStride, dstStride);

                    for (int y = 0; y < Height; y++)
                    {
                        Buffer.MemoryCopy(srcPtr + (y * srcStride), dstPtr + (y * dstStride), dstStride, bytesPerLine);
                    }
                }

                bmp.UnlockBits(bmpData);
                return bmp;
            }
            finally
            {
                _context.Unmap(_stagingTexture, 0);
            }
        }
        finally
        {
            desktopResource.Dispose();
            _duplication.ReleaseFrame();
        }
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
    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest, IntPtr hdcSrc, int xSrc, int ySrc, int rop);
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);
    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
    [DllImport("gdi32.dll")]
    private static extern bool GdiFlush();

    private const int SRCCOPY = 0x00CC0020;

    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }

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

        return bmp;
    }

    public void Dispose() { }
}
