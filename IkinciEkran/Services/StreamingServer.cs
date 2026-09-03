using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IkinciEkran.Services;

public class StreamingServer : IDisposable
{
    private readonly ConcurrentDictionary<Guid, NetworkStream> _clients = new();
    private TcpListener? _tcpListener;
    private CancellationTokenSource? _cts;
    private volatile bool _isRunning = false;

    public event Action<int>? ClientCountChanged;
    public event Action<string>? LogMessage;

    public bool IsRunning => _isRunning;
    public int Port { get; private set; } = 8080;
    public string LocalIp { get; private set; } = "127.0.0.1";
    public string StreamUrl => $"http://{LocalIp}:{Port}";
    public int ConnectedClientsCount => _clients.Count;

    public void Start(int port = 8080)
    {
        if (_isRunning) return;

        Port = port;
        LocalIp = GetLocalIpAddress();

        try
        {
            _tcpListener = new TcpListener(IPAddress.Any, port);
            _tcpListener.Start();
            _isRunning = true;
            _cts = new CancellationTokenSource();

            LogMessage?.Invoke($"Yayın Sunucusu Başlatıldı: {StreamUrl}");
            Task.Run(() => AcceptClientsLoop(_tcpListener, _cts.Token));
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Sunucu başlatma hatası: {ex.Message}");
            throw;
        }
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;
        _cts?.Cancel();

        try
        {
            _tcpListener?.Stop();
        }
        catch { }

        foreach (var kvp in _clients)
        {
            try { kvp.Value.Dispose(); } catch { }
        }
        _clients.Clear();
        ClientCountChanged?.Invoke(0);

        _cts?.Dispose();
        _cts = null;
        _tcpListener = null;

        LogMessage?.Invoke("Yayın Sunucusu Durduruldu.");
    }

    public void BroadcastFrame(byte[] jpegBytes)
    {
        if (!_isRunning || _clients.IsEmpty) return;

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
                if (_clients.TryRemove(clientId, out var removedStream))
                {
                    try { removedStream.Dispose(); } catch { }
                    ClientCountChanged?.Invoke(_clients.Count);
                    LogMessage?.Invoke($"İstemci ayrıldı. Kalan: {_clients.Count}");
                }
            }
        }
    }

    private async Task AcceptClientsLoop(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested && _isRunning)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(token);
                client.NoDelay = true; // Disable Nagle's algorithm for low-latency zero-lag
                _ = Task.Run(() => HandleClientAsync(client));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!_isRunning) break;
                LogMessage?.Invoke($"İstemci kabul hatası: {ex.Message}");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        var clientId = Guid.NewGuid();
        var stream = client.GetStream();

        try
        {
            byte[] buf = new byte[2048];
            int read = await stream.ReadAsync(buf, 0, buf.Length);
            string req = Encoding.ASCII.GetString(buf, 0, read);

            string responseHeader =
                "HTTP/1.1 200 OK\r\n" +
                "Connection: close\r\n" +
                "Server: IkinciEkran\r\n" +
                "Cache-Control: no-cache, no-store, must-revalidate, pre-check=0, post-check=0, max-age=0\r\n" +
                "Pragma: no-cache\r\n" +
                "Content-Type: multipart/x-mixed-replace; boundary=--frame\r\n" +
                "Access-Control-Allow-Origin: *\r\n\r\n";

            byte[] resHeaderBytes = Encoding.ASCII.GetBytes(responseHeader);
            await stream.WriteAsync(resHeaderBytes, 0, resHeaderBytes.Length);

            _clients.TryAdd(clientId, stream);
            ClientCountChanged?.Invoke(_clients.Count);
            LogMessage?.Invoke($"Yeni Cihaz Bağlandı! Aktif İstemciler: {_clients.Count}");
        }
        catch
        {
            client.Dispose();
        }
    }

    public static string GetLocalIpAddress()
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

    public void Dispose()
    {
        Stop();
    }
}
