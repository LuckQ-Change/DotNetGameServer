using System.Net.Sockets;
using Google.Protobuf;
using Shared;
using Core.Config;
using DotNetGameServer;
using System;
using System.Threading;
using R3;

namespace Core.Components;

/// <summary>
/// 目录心跳客户端组件：定期向目录服务器上报本服务状态。
/// </summary>
public class HeartbeatClientComponent : IServerComponent
{
    private readonly Action<string> _log;
    private readonly Func<int?> _portProvider;
    private readonly Func<int>? _playerCountProvider;
    private readonly Func<string>? _statusProvider;
    private CancellationTokenSource? _cts;
    private IDisposable? _hbSubscription;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private CancellationTokenSource? _connCts;
    private Task? _recvTask;

    private readonly SimpleConfigLoader.HeartbeatSettings _settings;
    private readonly string _type;
    private readonly string _idPrefix;
    private volatile int _lastResolvedPort;
    private string? _secret;

    /// <summary>
    /// 组件名称，用于日志与诊断标识。
    /// </summary>
    public string Name => "HeartbeatClient";

    /// <summary>
    /// 构造心跳客户端组件。
    /// </summary>
    /// <param name="log">日志回调。</param>
    /// <param name="type">服务类型标识（如 battle/resource/logic）。</param>
    /// <param name="portProvider">当前监听端口提供器。</param>
    /// <param name="playerCountProvider">玩家数量提供器，可选。</param>
    /// <param name="statusProvider">状态提供器，可选。</param>
    /// <param name="baseDir">配置基准目录。</param>
    public HeartbeatClientComponent(Action<string> log, string type, Func<int?> portProvider, Func<int>? playerCountProvider = null, Func<string>? statusProvider = null, string? baseDir = null)
    {
        _log = log;
        _type = type;
        _portProvider = portProvider;
        _playerCountProvider = playerCountProvider;
        _statusProvider = statusProvider;
        _settings = SimpleConfigLoader.LoadHeartbeatSettings(baseDir) ?? new Config.SimpleConfigLoader.HeartbeatSettings();
        _idPrefix = type; 
    }

    /// <summary>
    /// 初始化心跳循环。
    /// </summary>
    public Task InitializeAsync()
    {
        _cts = new CancellationTokenSource();
        // 缓存密钥：环境变量优先，其次配置文件；非开发环境不允许使用默认密钥
        var sec = SimpleConfigLoader.LoadDirectoryTokenSecret(AppContext.BaseDirectory);
        if (string.IsNullOrWhiteSpace(sec))
        {
            if (IsDevelopment())
            {
                _secret = "dev-secret";
            }
            else
            {
                throw new InvalidOperationException("HeartbeatClient 需要配置目录密钥 (directory.tokenSecret)。");
            }
        }
        else
        {
            _secret = sec;
        }
        var interval = TimeSpan.FromSeconds(Math.Max(1, _settings.IntervalSeconds));
        _hbSubscription = Observable.Interval(interval)
            .Subscribe(_ =>
            {
                var token = _cts!.Token;
                Task.Run(() => SendHeartbeatTickAsync(token), token);
            });
        return Task.CompletedTask;
    }

    /// <summary>
    /// 停止心跳循环并释放资源。
    /// </summary>
    public Task DestroyAsync()
    {
        try { _cts?.Cancel(); }
        catch (OperationCanceledException)
        {
            // 循环已取消
        }
        catch (Exception)
        {
            // 非关键异常，忽略以完成销毁流程。
        }
        try { _hbSubscription?.Dispose(); }
        catch
        {
            // 释放订阅失败可忽略：不影响销毁流程
        }
        _hbSubscription = null;
        try { _connCts?.Cancel(); } catch { }
        try { _stream?.Dispose(); } catch { }
        try { _client?.Dispose(); } catch { }
        _recvTask = null;
        _connCts = null;
        _stream = null;
        _client = null;
        try { _sendLock.Dispose(); } catch { }
        _cts = null;
        return Task.CompletedTask;
    }

    private async Task SendHeartbeatTickAsync(CancellationToken token)
    {
        int port = _portProvider?.Invoke() ?? 0;
        if (port <= 0 || port > 65535)
        {
            _log("[目录] 心跳等待：服务端口未就绪");
            return;
        }

        _lastResolvedPort = port;
        var req = new HeartbeatRequest
        {
            Id = _idPrefix + ":" + port,
            Type = _type,
            Region = _settings.Region,
            Host = _settings.Host,
            Port = port,
            PlayerCount = _playerCountProvider?.Invoke() ?? 0,
            Capacity = _settings.Capacity,
            Status = _statusProvider?.Invoke() ?? "online"
        };
        _log($"[心跳] tick: {req.Id} -> {_settings.DirectoryHost}:{_settings.DirectoryPort} players={req.PlayerCount} capacity={req.Capacity} status={req.Status}");
        try
        {
            await SendHeartbeatAsync(_settings.DirectoryHost, _settings.DirectoryPort, req, token);
            _log("[心跳] sent");
        }
        catch (Exception ex)
        {
            _log($"[心跳] send failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 通过 TCP 发送心跳信封到目录服务器。
    /// </summary>
    private async Task SendHeartbeatAsync(string host, int port, HeartbeatRequest req, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            await EnsureConnectedAsync(host, port, ct);
            var env = new Envelope { Type = "HeartbeatRequest", Payload = ByteString.CopyFrom(req.ToByteArray()) };
            try
            {
                var secret = _secret ?? "dev-secret";
                env.Token = TokenSigner.ComposeHeartbeatToken(req.Id, secret);
            }
            catch (Exception)
            {
            }
            var bytes = env.ToByteArray();
            var len = BitConverter.GetBytes(bytes.Length);
            var stream = _stream!;
            await stream.WriteAsync(len.AsMemory(0, 4), ct);
            await stream.WriteAsync(bytes.AsMemory(), ct);
            await stream.FlushAsync(ct);
        }
        catch
        {
            SafeCloseConnection();
            throw;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// 从网络流精确读取指定字节数；对端关闭则返回已读取长度。
    /// </summary>
    private static async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken token)
    {
        var total = 0;
        while (total < count)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(offset + total, count - total), token);
            if (n == 0) return total;
            total += n;
        }
        return total;
    }

    /// <summary>
    /// 获取当前服务器ID（type:port）。若端口未就绪则返回 <c>null</c>。
    /// </summary>
    public string? TryGetCurrentServerId()
    {
        var port = _portProvider?.Invoke() ?? _lastResolvedPort;
        if (port <= 0 || port > 65535) return null;
        return _idPrefix + ":" + port;
    }

    private static bool IsDevelopment()
    {
        var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                  ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                  ?? string.Empty;
        return string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase);
    }

    private async Task EnsureConnectedAsync(string host, int port, CancellationToken ct)
    {
        if (_client != null && _client.Connected && _stream != null) return;
        SafeCloseConnection();
        var client = new TcpClient();
        try { client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true); } catch { }
        await client.ConnectAsync(host, port, ct);
        _client = client;
        _stream = client.GetStream();
        _connCts = CancellationTokenSource.CreateLinkedTokenSource(_cts!.Token);
        _recvTask = Task.Run(() => ReceiveLoopAsync(_connCts.Token));
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        var stream = _stream;
        if (stream == null) return;
        try
        {
            var lenBuf = new byte[4];
            while (!token.IsCancellationRequested)
            {
                var r = await ReadExactAsync(stream, lenBuf, 0, 4, token);
                if (r < 4) break;
                var respLen = BitConverter.ToInt32(lenBuf, 0);
                if (respLen <= 0 || respLen > 8 * 1024 * 1024) break;
                var buf = new byte[respLen];
                var rr = await ReadExactAsync(stream, buf, 0, respLen, token);
                if (rr < respLen) break;
            }
        }
        catch
        {
        }
        finally
        {
            SafeCloseConnection();
        }
    }

    private void SafeCloseConnection()
    {
        try { _connCts?.Cancel(); } catch { }
        try { _stream?.Dispose(); } catch { }
        try { _client?.Dispose(); } catch { }
        _recvTask = null;
        _connCts = null;
        _stream = null;
        _client = null;
    }
}
