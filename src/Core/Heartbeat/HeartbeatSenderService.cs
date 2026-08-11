using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

using Core.Components;
using Core.Configuration;
using Core.Networking;

using DotNetGameServer;

using Google.Protobuf;

using Microsoft.Extensions.Logging;

namespace Core.Heartbeat;

public class HeartbeatSenderService : IServerComponent, IServerComponentHealth
{
    private readonly ServiceRegistrationOptions _options;
    private readonly ILogger? _logger;
    private readonly string _secret;
    private readonly IServerLoadProvider _loadProvider;
    private CancellationTokenSource? _cts;
    private Task? _task;
    private long _lastSuccessUnixSeconds;

    public string Name => "HeartbeatSender";

    public HeartbeatSenderService(
        ServiceRegistrationOptions options,
        string secret,
        IServerLoadProvider loadProvider,
        ILogger? logger = null)
    {
        _options = options;
        _secret = secret;
        _loadProvider = loadProvider;
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
        var cts = _cts;
        var task = _task;
        if (cts is null || task is null) return;
        await cts.CancelAsync();
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // 正常关闭。
        }
        finally
        {
            try
            {
                using var unregisterCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await SendUnregisterAsync(unregisterCts.Token);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("服务主动注销失败，将由注册中心按 TTL 清理: {Message}", ex.Message);
            }
            cts.Dispose();
            _cts = null;
            _task = null;
        }
    }

    private async Task RunHeartbeatLoop(CancellationToken token)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.IntervalSeconds));
        var consecutiveFailures = 0;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await SendHeartbeatAsync(token);
                consecutiveFailures = 0;
                await Task.Delay(interval, token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                consecutiveFailures++;
                _logger?.LogWarning("心跳发送失败: {Message}", ex.Message);
                var backoffSeconds = Math.Min(30, Math.Pow(2, Math.Min(consecutiveFailures - 1, 5)));
                var jitterMilliseconds = Random.Shared.Next(0, 500);
                await Task.Delay(
                    TimeSpan.FromSeconds(backoffSeconds) + TimeSpan.FromMilliseconds(jitterMilliseconds),
                    token);
            }
        }
    }

    private async Task SendHeartbeatAsync(CancellationToken token)
    {
        var serverId = string.IsNullOrWhiteSpace(_options.ServerId)
            ? $"{_options.ServerType}:{_options.Host}:{_options.Port}"
            : _options.ServerId;

        var heartbeat = new HeartbeatRequest
        {
            ServerId = serverId,
            ServerType = _options.ServerType,
            Host = _options.Host,
            Port = _options.Port,
            PlayerCount = _loadProvider.PlayerCount,
            Capacity = _loadProvider.Capacity,
            Region = _options.Region,
            Version = _options.Version,
            TokenAudience = _options.TokenAudience,
            TlsEnabled = _options.TlsEnabled,
            TlsServerName = _options.TlsServerName,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        var request = new envelope
        {
            Type = HeartbeatRequest.Descriptor.FullName,
            Payload = heartbeat.ToByteString(),
            Token = Shared.TokenSigner.ComposeServiceToken(
                serverId,
                _options.DiscoveryAudience,
                _secret,
                _options.IntervalSeconds * 3),
            Channel = CommunicationChannel.Internal
        };
        var responseEnvelope = await SendAsync(request, token);
        if (responseEnvelope.Channel != CommunicationChannel.Internal)
            throw new InvalidDataException("服务发现心跳响应通道不合法。");
        if (!string.Equals(responseEnvelope.Type, HeartbeatResponse.Descriptor.FullName, StringComparison.Ordinal))
            throw new InvalidDataException("服务发现心跳响应类型不合法。");
        if (responseEnvelope.ErrorCode != 0)
            throw new InvalidOperationException($"服务发现节点拒绝心跳: {responseEnvelope.ErrorMessage}");
        var response = HeartbeatResponse.Parser.ParseFrom(responseEnvelope.Payload);
        if (!response.Accepted)
            throw new InvalidOperationException($"服务发现节点拒绝心跳: {response.Message}");

        Interlocked.Exchange(ref _lastSuccessUnixSeconds, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        _logger?.LogDebug("心跳已发送: {ServerId}", serverId);
    }

    private async Task SendUnregisterAsync(CancellationToken cancellationToken)
    {
        var serverId = GetServerId();
        var request = new envelope
        {
            Type = ServerUnregisterRequest.Descriptor.FullName,
            Payload = new ServerUnregisterRequest { ServerId = serverId }.ToByteString(),
            Token = Shared.TokenSigner.ComposeServiceToken(
                serverId,
                _options.DiscoveryAudience,
                _secret,
                Math.Max(1, _options.IntervalSeconds * 3)),
            RequestId = Guid.NewGuid().ToString("N"),
            Channel = CommunicationChannel.Internal
        };
        var responseEnvelope = await SendAsync(request, cancellationToken);
        if (responseEnvelope.ErrorCode != 0)
            throw new InvalidOperationException($"服务发现节点拒绝注销: {responseEnvelope.ErrorMessage}");
        if (!string.Equals(responseEnvelope.Type, ServerUnregisterResponse.Descriptor.FullName, StringComparison.Ordinal))
            throw new InvalidDataException("服务发现注销响应类型不合法。");
        var response = ServerUnregisterResponse.Parser.ParseFrom(responseEnvelope.Payload);
        if (!response.Success)
            throw new InvalidOperationException($"服务发现节点拒绝注销: {response.Message}");
    }

    private async Task<envelope> SendAsync(envelope request, CancellationToken cancellationToken)
    {
        using var client = new TcpClient { NoDelay = true };
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
        await client.ConnectAsync(_options.DiscoveryHost, _options.DiscoveryPort, timeoutCts.Token);

        Stream stream = client.GetStream();
        SslStream? secureStream = null;
        try
        {
            if (_options.DiscoveryTlsEnabled)
            {
                secureStream = new SslStream(stream, leaveInnerStreamOpen: false);
                await secureStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = string.IsNullOrWhiteSpace(_options.DiscoveryTlsServerName)
                        ? _options.DiscoveryHost
                        : _options.DiscoveryTlsServerName,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                }, timeoutCts.Token);
                stream = secureStream;
            }

            await ProtobufFrameCodec.WriteEnvelopeAsync(stream, request, 64 * 1024, timeoutCts.Token);
            return await ProtobufFrameCodec.ReadEnvelopeAsync(stream, 64 * 1024, timeoutCts.Token)
                ?? throw new EndOfStreamException("服务发现节点在返回响应前关闭连接。");
        }
        finally
        {
            if (secureStream is not null) await secureStream.DisposeAsync();
        }
    }

    private string GetServerId() => string.IsNullOrWhiteSpace(_options.ServerId)
        ? $"{_options.ServerType}:{_options.Host}:{_options.Port}"
        : _options.ServerId;

    public Task<ServerComponentHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var lastSuccess = Interlocked.Read(ref _lastSuccessUnixSeconds);
        var maxAge = Math.Max(3, _options.IntervalSeconds * 3);
        var age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - lastSuccess;
        var healthy = lastSuccess > 0 && age <= maxAge;
        return Task.FromResult(new ServerComponentHealth(
            Name,
            healthy,
            healthy ? $"last_success_age={age}s" : "service_discovery_heartbeat_unavailable"));
    }
}
