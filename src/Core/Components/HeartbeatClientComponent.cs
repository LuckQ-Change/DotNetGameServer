using System.Net.Sockets;
using Google.Protobuf;
using Shared;
using Core.Config;
using DotNetGameServer;
using System;

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
        _ = Task.Run(() => HeartbeatLoop(_cts.Token));
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
        _cts = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 心跳发送主循环：按配置节拍向目录服务器发送 <see cref="HeartbeatRequest"/>。
    /// </summary>
    private async Task HeartbeatLoop(CancellationToken token)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _settings.IntervalSeconds));
        while (!token.IsCancellationRequested)
        {
            try
            {
                int port = _portProvider?.Invoke() ?? 0;
                if (port <= 0 || port > 65535)
                {
                    _log("[目录] 心跳等待：服务端口未就绪"); // 端口未就绪，暂不发送心跳
                }
                else
                {
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
                    await SendHeartbeatAsync(_settings.DirectoryHost, _settings.DirectoryPort, req, token);
                }
            }
            catch (Exception ex)
            {
                _log($"[目录] 心跳发送异常: {ex.Message}");
            }

            try { await Task.Delay(interval, token); }
            catch (OperationCanceledException)
            {
                // 取消心跳循环
                break;
            }
            catch (Exception)
            {
                // 非关键异常，忽略以保持心跳循环。
            }
        }
    }

    /// <summary>
    /// 通过 TCP 发送心跳信封到目录服务器。
    /// </summary>
    private async Task SendHeartbeatAsync(string host, int port, HeartbeatRequest req, CancellationToken ct)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, ct);
        using var stream = client.GetStream();
        var env = new Envelope { Type = "HeartbeatRequest", Payload = ByteString.CopyFrom(req.ToByteArray()) };
        // 生成心跳签名令牌（绑定到 serverId）
        try
        {
            var secret = _secret ?? "dev-secret"; // 若异常情况下为空，在开发模式仍允许默认值
            env.Token = TokenSigner.ComposeHeartbeatToken(req.Id, secret, 300);
        }
        catch (Exception)
        {
            // 心跳令牌生成失败时发送未签名心跳，目录服务可按未认证处理。
        }
        var bytes = env.ToByteArray();
        var len = BitConverter.GetBytes(bytes.Length);
        await stream.WriteAsync(len.AsMemory(0, 4), ct);
        await stream.WriteAsync(bytes.AsMemory(), ct);
        await stream.FlushAsync(ct);

        // 读取响应（可选）
        var lenBuf = new byte[4];
        var read = await ReadExactAsync(stream, lenBuf, 0, 4, ct);
        if (read == 0) return;
        var respLen = BitConverter.ToInt32(lenBuf, 0);
        var buf = new byte[respLen];
        await ReadExactAsync(stream, buf, 0, respLen, ct);
        // 可解析 HeartbeatResponse 但当前无需使用
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
}
