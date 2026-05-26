using System.Net.Sockets;
using Core.Configuration;
using DotNetGameServer;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Core.Heartbeat;

public class HeartbeatSenderService : IServerComponent
{
    private readonly HeartbeatOptions _options;
    private readonly ILogger? _logger;
    private readonly string _secret;
    private CancellationTokenSource? _cts;
    private Task? _task;

    public string Name => "HeartbeatSender";

    public HeartbeatSenderService(HeartbeatOptions options, string secret, ILogger? logger = null)
    {
        _options = options;
        _secret = secret;
        _logger = logger;
    }

    public Task InitializeAsync()
    {
        _cts = new CancellationTokenSource();
        _task = RunHeartbeatLoop(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task DestroyAsync()
    {
        await _cts?.CancelAsync()!;
        try { await (_task ?? Task.CompletedTask); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Heartbeat] 等待心跳任务失败: {ex.Message}"); }
    }

    private async Task RunHeartbeatLoop(CancellationToken token)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.IntervalSeconds));
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, token);
                await SendHeartbeatAsync(token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger?.LogWarning("心跳发送失败: {Message}", ex.Message);
            }
        }
    }

    private async Task SendHeartbeatAsync(CancellationToken token)
    {
        var serverId = string.IsNullOrWhiteSpace(_options.ServerId)
            ? $"{_options.ServerType}:{_options.Port}"
            : _options.ServerId;

        var heartbeat = new HeartbeatRequest
        {
            ServerId = serverId,
            ServerType = _options.ServerType,
            Host = _options.Host,
            Port = _options.Port,
            PlayerCount = 0,
            Capacity = _options.Capacity,
            Region = _options.Region,
            Version = "1.0.0",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        var payload = heartbeat.ToByteArray();
        using var client = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        await client.ConnectAsync(_options.DirectoryHost, _options.DirectoryPort, cts.Token);

        var stream = client.GetStream();
        var envBytes = new envelope
        {
            Type = "HeartbeatRequest",
            Payload = ByteString.CopyFrom(payload),
            Token = Shared.TokenSigner.ComposeHeartbeatToken(serverId, _secret, _options.IntervalSeconds * 3)
        }.ToByteArray();

        // 长度前缀使用与服务端读取一致的主机字节序（BaseGameServer 用 BitConverter.ToInt32 解析）。
        var lenBuf = BitConverter.GetBytes(envBytes.Length);
        await stream.WriteAsync(lenBuf, cts.Token);
        await stream.WriteAsync(envBytes, cts.Token);
        _logger?.LogDebug("心跳已发送: {ServerId}", serverId);
    }
}
