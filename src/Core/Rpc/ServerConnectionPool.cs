using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

using Core.Configuration;
using Core.Networking;

using DotNetGameServer;

namespace Core.Rpc;

/// <summary>复用服务器间长连接，并通过请求 ID 关联并发响应。</summary>
public interface IServerConnectionPool : IAsyncDisposable
{
    Task<envelope> SendAsync(
        ServerEndpoint endpoint,
        envelope request,
        TimeSpan requestTimeout,
        CancellationToken cancellationToken = default);
}

public sealed class ServerConnectionPool : IServerConnectionPool
{
    private readonly ConcurrentDictionary<ServerEndpoint, ServerConnection> _connections = new();
    private readonly ServerRpcOptions _options;
    private readonly SemaphoreSlim _poolGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _cleanupTask;
    private int _disposed;

    public ServerConnectionPool(ServerRpcOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _cleanupTask = RunCleanupLoopAsync(_lifetime.Token);
    }

    /// <summary>当前缓存的跨服连接数，供健康检查和容量诊断使用。</summary>
    public int PooledConnectionCount => _connections.Count;

    public async Task<envelope> SendAsync(
        ServerEndpoint endpoint,
        envelope request,
        TimeSpan requestTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (requestTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(requestTimeout), "请求超时必须大于 0。");
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        while (true)
        {
            var connection = await GetLeasedConnectionAsync(endpoint, cancellationToken);

            try
            {
                return await connection.SendAsync(request, requestTimeout, cancellationToken);
            }
            catch (Exception ex) when (IsConnectionFailure(ex))
            {
                await RemoveAsync(endpoint, connection);
                throw;
            }
            finally
            {
                connection.Release();
            }
        }
    }

    private async Task<ServerConnection> GetLeasedConnectionAsync(
        ServerEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        ServerConnection? evicted = null;
        await _poolGate.WaitAsync(cancellationToken);
        try
        {
            while (true)
            {
                if (_connections.TryGetValue(endpoint, out var existing))
                {
                    if (existing.TryAcquire()) return existing;
                    _connections.TryRemove(
                        new KeyValuePair<ServerEndpoint, ServerConnection>(endpoint, existing));
                    evicted = existing;
                }

                if (_connections.Count >= Math.Max(1, _options.MaxPooledConnections))
                {
                    var candidate = _connections
                        .OrderBy(item => item.Value.LastUsedTimestamp)
                        .FirstOrDefault(item => item.Value.CanEvict);
                    if (candidate.Value is null)
                        throw new IOException("跨服连接池已满，且没有可回收的空闲连接。");
                    if (_connections.TryRemove(candidate))
                        evicted = candidate.Value;
                    else
                        continue;
                }

                var created = new ServerConnection(endpoint, _options);
                if (!_connections.TryAdd(endpoint, created))
                {
                    await created.DisposeAsync();
                    continue;
                }
                if (created.TryAcquire()) return created;
            }
        }
        finally
        {
            _poolGate.Release();
            if (evicted is not null) await evicted.DisposeAsync();
        }
    }

    private static bool IsConnectionFailure(Exception exception) =>
        exception is IOException or SocketException or AuthenticationException or EndOfStreamException;

    private async Task RemoveAsync(ServerEndpoint endpoint, ServerConnection connection)
    {
        _connections.TryRemove(new KeyValuePair<ServerEndpoint, ServerConnection>(endpoint, connection));
        await connection.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _lifetime.CancelAsync();
        try { await _cleanupTask; }
        catch (OperationCanceledException)
        {
            // 连接池释放时的正常取消。
        }
        var connections = _connections.ToArray();
        _connections.Clear();
        foreach (var item in connections)
            await item.Value.DisposeAsync();
        _lifetime.Dispose();
        _poolGate.Dispose();
    }

    private async Task RunCleanupLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(
            Math.Max(1, _options.PoolCleanupIntervalSeconds)));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var idleTimeout = TimeSpan.FromSeconds(Math.Max(1, _options.IdleConnectionTimeoutSeconds));
            var evicted = new List<ServerConnection>();
            await _poolGate.WaitAsync(cancellationToken);
            try
            {
                foreach (var item in _connections.ToArray())
                {
                    if (!item.Value.IsIdle(idleTimeout)) continue;
                    if (_connections.TryRemove(item)) evicted.Add(item.Value);
                }
            }
            finally
            {
                _poolGate.Release();
            }
            foreach (var connection in evicted)
                await connection.DisposeAsync();
        }
    }

    private sealed class ServerConnection : IAsyncDisposable
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<envelope>> _pending = new();
        private readonly SemaphoreSlim _connectGate = new(1, 1);
        private readonly SemaphoreSlim _writeGate = new(1, 1);
        private readonly ServerEndpoint _endpoint;
        private readonly ServerRpcOptions _options;
        private TcpClient? _client;
        private Stream? _stream;
        private CancellationTokenSource? _lifetime;
        private Task? _receiveTask;
        private long _lastUsedTimestamp = Stopwatch.GetTimestamp();
        private int _activeRequests;
        private int _faulted;
        private int _disposed;

        public ServerConnection(ServerEndpoint endpoint, ServerRpcOptions options)
        {
            _endpoint = endpoint;
            _options = options;
        }

        public bool IsAvailable => Volatile.Read(ref _faulted) == 0 && Volatile.Read(ref _disposed) == 0;
        public long LastUsedTimestamp => Interlocked.Read(ref _lastUsedTimestamp);
        public bool CanEvict => Volatile.Read(ref _activeRequests) == 0;

        public bool TryAcquire()
        {
            if (!IsAvailable) return false;
            Interlocked.Increment(ref _activeRequests);
            if (IsAvailable)
            {
                Touch();
                return true;
            }
            Release();
            return false;
        }

        public void Release()
        {
            Touch();
            Interlocked.Decrement(ref _activeRequests);
        }

        public bool IsIdle(TimeSpan timeout) =>
            CanEvict && Stopwatch.GetElapsedTime(LastUsedTimestamp) >= timeout;

        private void Touch() => Interlocked.Exchange(ref _lastUsedTimestamp, Stopwatch.GetTimestamp());

        public async Task<envelope> SendAsync(
            envelope request,
            TimeSpan requestTimeout,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.RequestId))
                throw new ArgumentException("跨服请求必须包含请求 ID。", nameof(request));
            await EnsureConnectedAsync(cancellationToken);

            var completion = new TaskCompletionSource<envelope>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pending.TryAdd(request.RequestId, completion))
                throw new InvalidOperationException($"请求 ID 重复: {request.RequestId}");

            try
            {
                await _writeGate.WaitAsync(cancellationToken);
                try
                {
                    var stream = _stream ?? throw new IOException("服务器连接尚未建立。");
                    await ProtobufFrameCodec.WriteEnvelopeAsync(
                        stream,
                        request,
                        Math.Max(1024, _options.MaxFrameSizeBytes),
                        cancellationToken);
                }
                finally
                {
                    _writeGate.Release();
                }

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(requestTimeout);
                try
                {
                    return await completion.Task.WaitAsync(timeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException($"跨服请求等待响应超时: {_endpoint.ServerId}/{request.RequestId}");
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or AuthenticationException)
            {
                Fault(ex);
                throw;
            }
            finally
            {
                _pending.TryRemove(request.RequestId, out _);
            }
        }

        private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
        {
            if (_stream is not null && IsAvailable) return;
            await _connectGate.WaitAsync(cancellationToken);
            try
            {
                if (_stream is not null && IsAvailable) return;
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                if (Volatile.Read(ref _faulted) != 0)
                    throw new IOException($"服务器连接已失效: {_endpoint.ServerId}");

                using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectTimeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(100, _options.ConnectTimeoutMs)));
                var client = new TcpClient { NoDelay = true };
                try
                {
                    await client.ConnectAsync(_endpoint.Host, _endpoint.Port, connectTimeout.Token);
                    Stream stream = client.GetStream();
                    if (_endpoint.TlsEnabled)
                    {
                        var secureStream = new SslStream(stream, leaveInnerStreamOpen: false);
                        await secureStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                        {
                            TargetHost = string.IsNullOrWhiteSpace(_endpoint.TlsServerName)
                                ? _endpoint.Host
                                : _endpoint.TlsServerName,
                            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                            CertificateRevocationCheckMode = _options.CheckCertificateRevocation
                                ? X509RevocationMode.Online
                                : X509RevocationMode.NoCheck
                        }, connectTimeout.Token);
                        stream = secureStream;
                    }

                    _client = client;
                    _stream = stream;
                    _lifetime = new CancellationTokenSource();
                    _receiveTask = ReceiveLoopAsync(stream, _lifetime.Token);
                }
                catch
                {
                    client.Dispose();
                    throw;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"连接服务器超时: {_endpoint.Host}:{_endpoint.Port}");
            }
            finally
            {
                _connectGate.Release();
            }
        }

        private async Task ReceiveLoopAsync(Stream stream, CancellationToken cancellationToken)
        {
            Exception? failure = null;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var response = await ProtobufFrameCodec.ReadEnvelopeAsync(
                        stream,
                        Math.Max(1024, _options.MaxFrameSizeBytes),
                        cancellationToken);
                    if (response is null)
                    {
                        failure = new EndOfStreamException($"服务器主动关闭连接: {_endpoint.ServerId}");
                        break;
                    }
                    if (!string.IsNullOrWhiteSpace(response.RequestId)
                        && _pending.TryRemove(response.RequestId, out var completion))
                        completion.TrySetResult(response);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 正常关闭。
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            if (failure is not null) Fault(failure);
        }

        private void Fault(Exception exception)
        {
            if (Interlocked.Exchange(ref _faulted, 1) != 0) return;
            TryCancel(_lifetime);
            TryDispose(_stream);
            TryDispose(_client);
            foreach (var pending in _pending.ToArray())
            {
                if (_pending.TryRemove(pending.Key, out var completion))
                    completion.TrySetException(exception);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            await TryCancelAsync(_lifetime);
            TryDispose(_stream);
            TryDispose(_client);
            var receiveTask = _receiveTask;
            if (receiveTask is not null)
            {
                try
                {
                    await receiveTask;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ServerRpc] 等待接收任务结束失败: {ex.Message}");
                }
            }
            Fault(new ObjectDisposedException(nameof(ServerConnection)));
            _lifetime?.Dispose();
            _connectGate.Dispose();
            _writeGate.Dispose();
        }

        private static void TryCancel(CancellationTokenSource? source)
        {
            try { source?.Cancel(); }
            catch (ObjectDisposedException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ServerRpc] 取消已释放的连接令牌: {ex.Message}");
            }
        }

        private static async Task TryCancelAsync(CancellationTokenSource? source)
        {
            try { await (source?.CancelAsync() ?? Task.CompletedTask); }
            catch (ObjectDisposedException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ServerRpc] 取消已释放的连接令牌: {ex.Message}");
            }
        }

        private static void TryDispose(IDisposable? disposable)
        {
            try { disposable?.Dispose(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ServerRpc] 释放连接资源失败: {ex.Message}");
            }
        }
    }
}
