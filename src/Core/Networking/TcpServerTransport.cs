using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

using Core.Configuration;
using Core.MessagePipeline;
using Core.Telemetry;

using Microsoft.Extensions.Logging;

namespace Core.Networking;

/// <summary>
/// 管理 TCP 监听器、连接槽、活动连接任务以及网络维护任务。
/// </summary>
/// <remarks>
/// 该类型只关心传输层生命周期；业务请求的解析和分发由
/// <see cref="ClientConnectionSession"/> 完成。
/// </remarks>
internal sealed class TcpServerTransport
{
    private readonly BaseGameServer _server;
    private readonly ServerRuntimeOptions _options;
    private readonly MessageDelegate _pipeline;
    private readonly NetworkAccessGuard _accessGuard;
    private readonly SemaphoreSlim _connectionSlots;
    private readonly ConcurrentDictionary<long, Task> _clientTasks = new();

    private TcpListener? _listener;
    private X509Certificate2? _serverCertificate;
    private Task? _acceptTask;
    private Task? _maintenanceTask;
    private long _nextClientTaskId;
    private int _currentConnections;

    public TcpServerTransport(
        BaseGameServer server,
        ServerRuntimeOptions options,
        MessageDelegate pipeline)
    {
        _server = server;
        _options = options;
        _pipeline = pipeline;
        _accessGuard = new NetworkAccessGuard(options, server.Log);
        _connectionSlots = new SemaphoreSlim(
            options.MaxConcurrentConnections,
            options.MaxConcurrentConnections);
    }

    public bool IsRunning => _listener is not null;
    public int? ListeningPort { get; private set; }
    public int CurrentConnections => Volatile.Read(ref _currentConnections);

    /// <summary>同步完成证书加载和端口绑定，然后启动后台接入循环。</summary>
    public void Start(int port, CancellationToken cancellationToken)
    {
        if (_listener is not null)
            throw new InvalidOperationException("TCP 传输层已经启动。");

        try
        {
            _serverCertificate = LoadServerCertificate();
            _listener = new TcpListener(_options.ListenAddress, port);
            _listener.Start(_options.ListenBacklog);
            ListeningPort = (_listener.LocalEndpoint as IPEndPoint)?.Port ?? port;
            _acceptTask = AcceptLoopAsync(cancellationToken);
            _maintenanceTask = _accessGuard.RunMaintenanceLoopAsync(cancellationToken);
        }
        catch
        {
            _listener?.Stop();
            _listener = null;
            ListeningPort = null;
            _serverCertificate?.Dispose();
            _serverCertificate = null;
            throw;
        }
    }

    /// <summary>
    /// 停止接入新连接，等待接入循环、现有连接和维护任务退出。
    /// </summary>
    public async Task StopAsync(CancellationToken shutdownToken)
    {
        StopAccepting();
        await WaitForAcceptLoopAsync(shutdownToken);
        await WaitForClientTasksAsync(shutdownToken);
        await WaitForMaintenanceLoopAsync(shutdownToken);

        _listener = null;
        _acceptTask = null;
        _maintenanceTask = null;
        ListeningPort = null;
        _serverCertificate?.Dispose();
        _serverCertificate = null;
    }

    /// <summary>立即关闭监听套接字，使正在等待的 Accept 操作结束。</summary>
    public void StopAccepting()
    {
        try
        {
            _listener?.Stop();
        }
        catch (Exception exception)
        {
            // 停止监听失败不阻断后续连接和组件清理。
            _server.Log($"停止 TCP 监听器失败: {exception.Message}");
        }
    }

    private X509Certificate2? LoadServerCertificate()
    {
        if (!_options.Tls.Enabled) return null;
        if (string.IsNullOrWhiteSpace(_options.Tls.CertificatePath))
            throw new InvalidOperationException("TLS 已启用但未配置 CertificatePath。");

        const X509KeyStorageFlags certificateFlags = X509KeyStorageFlags.MachineKeySet;
        var certificate = string.IsNullOrWhiteSpace(_options.Tls.CertificatePassword)
            ? new X509Certificate2(_options.Tls.CertificatePath, (string?)null, certificateFlags)
            : new X509Certificate2(
                _options.Tls.CertificatePath,
                _options.Tls.CertificatePassword,
                certificateFlags);
        _server.Log($"TLS 已启用，证书: {certificate.Subject}，指纹: {certificate.Thumbprint}");
        return certificate;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        var currentBackoffMs = _options.AcceptBackoffMs;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(cancellationToken);
                client.NoDelay = _options.NoDelay;
                var remoteEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "未知";
                var remoteIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address?.ToString() ?? remoteEndpoint;

                if (_accessGuard.IsBlacklisted(remoteIp))
                {
                    CloseRejectedClient(client, $"拒绝黑名单连接: {remoteEndpoint}");
                    currentBackoffMs = _options.AcceptBackoffMs;
                    continue;
                }

                // WaitAsync(0) 原子占用连接槽，避免“先检查、后计数”产生超卖竞态。
                if (!await _connectionSlots.WaitAsync(0, cancellationToken))
                {
                    CloseRejectedClient(
                        client,
                        $"接受到连接但已达上限 {_options.MaxConcurrentConnections}，拒绝 {remoteEndpoint}");
                    continue;
                }

                TrackClient(client, cancellationToken);
                currentBackoffMs = _options.AcceptBackoffMs;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                _server.Log($"接受连接异常: {exception.Message}，将稍后重试");
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(currentBackoffMs), cancellationToken);
                    currentBackoffMs = Math.Min(
                        currentBackoffMs <= 0 ? _options.AcceptBackoffMs : currentBackoffMs * 2,
                        10_000);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private void TrackClient(TcpClient client, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _currentConnections);
        CoreTelemetry.RecordConnectionDelta(_server.ServerName, 1);

        var taskId = Interlocked.Increment(ref _nextClientTaskId);
        var clientTask = RunClientSessionAsync(client, cancellationToken);
        _clientTasks[taskId] = clientTask;
        _ = clientTask.ContinueWith(
            completedTask => _clientTasks.TryRemove(taskId, out Task? _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task RunClientSessionAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            var session = new ClientConnectionSession(
                _server,
                _options,
                _accessGuard,
                _pipeline,
                _serverCertificate);
            await session.RunAsync(client, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _currentConnections);
            CoreTelemetry.RecordConnectionDelta(_server.ServerName, -1);
            _connectionSlots.Release();
        }
    }

    private void CloseRejectedClient(TcpClient client, string reason)
    {
        try
        {
            _server.Log(reason);
            client.Close();
        }
        catch (Exception exception)
        {
            _server.Log($"关闭被拒绝的连接失败: {exception.Message}");
        }
    }

    private async Task WaitForAcceptLoopAsync(CancellationToken shutdownToken)
    {
        try
        {
            if (_acceptTask is not null)
                await _acceptTask.WaitAsync(shutdownToken);
        }
        catch (OperationCanceledException)
        {
            // 服务器取消或关停超时均由外层生命周期继续处理。
        }
        catch (Exception exception)
        {
            _server.Log($"等待接受连接任务结束失败: {exception.Message}");
        }
    }

    private async Task WaitForClientTasksAsync(CancellationToken shutdownToken)
    {
        var activeClients = _clientTasks.Values.ToArray();
        if (activeClients.Length == 0) return;

        try
        {
            await Task.WhenAll(activeClients).WaitAsync(shutdownToken);
        }
        catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
        {
            _server.Log($"等待客户端连接关闭超时，剩余任务数: {_clientTasks.Count}", LogLevel.Warning);
        }
        catch (Exception exception)
        {
            _server.Log($"等待客户端连接关闭失败: {exception.Message}", LogLevel.Warning);
        }
    }

    private async Task WaitForMaintenanceLoopAsync(CancellationToken shutdownToken)
    {
        try
        {
            if (_maintenanceTask is not null)
                await _maintenanceTask.WaitAsync(shutdownToken);
        }
        catch (OperationCanceledException)
        {
            // 服务器停止时维护循环应通过共享取消令牌退出。
        }
    }
}
