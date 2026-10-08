using System.Collections.Concurrent;

using Client.Discovery;
using Client.Transport;

using DotNetGameServer;

using Google.Protobuf;

using Shared;

namespace Client;

public sealed class GameNetworkClient : IAsyncDisposable
{
    private readonly IGameTransport _transport;
    private readonly IServiceDiscoveryProvider _discovery;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<envelope>> _pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<Action<envelope>>> _typedHandlers = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private CancellationTokenSource? _lifetime;
    private ServiceQuery? _lastQuery;
    private bool _disconnectRequested;
    private int _reconnecting;
    private AccessTokenGrant? _accessTokenGrant;
    private readonly SemaphoreSlim _tokenRefreshGate = new(1, 1);
    private int _tokenRefreshDepth;

    public GameNetworkClient(IGameTransport transport, IServiceDiscoveryProvider discovery)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
    }

    public static GameNetworkClient CreateTcp(
        IEnumerable<string> bootstrapUrls,
        int maxFrameBytes = 1024 * 1024) =>
        new(new TcpGameTransport(maxFrameBytes), new HttpServiceDiscoveryProvider(bootstrapUrls));

    public static GameNetworkClient CreateTcp(ServiceEndpoint endpoint, int maxFrameBytes = 1024 * 1024) =>
        new(new TcpGameTransport(maxFrameBytes), new StaticServiceDiscoveryProvider([endpoint]));

    public bool IsConnected => _transport.IsConnected;
    public ServiceEndpoint? CurrentEndpoint => _transport.CurrentEndpoint;
    public string AccessToken { get; set; } = string.Empty;
    public AccessTokenGrant? CurrentAccessToken => _accessTokenGrant;
    public Func<CancellationToken, Task<AccessTokenGrant?>>? TokenRefreshHandler { get; set; }
    public TimeSpan TokenRefreshLeadTime { get; set; } = TimeSpan.FromSeconds(120);
    public string ClientVersion { get; set; } = string.Empty;
    public bool RequireCompatibleVersion { get; set; } = true;
    public bool AutoHeartbeat { get; set; } = true;
    public bool AutoReconnect { get; set; }
    public int MaxReconnectAttempts { get; set; } = 3;
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public event Action<envelope>? NotificationReceived;
    public event Action<Exception>? ConnectionClosed;
    public event Action? Reconnected;

    public async Task<ServiceEndpoint> ConnectAsync(ServiceQuery query, CancellationToken cancellationToken)
    {
        _disconnectRequested = false;
        _lastQuery = query;
        return await ConnectCoreAsync(query, cancellationToken);
    }

    public async Task<TResponse> RequestAsync<TResponse>(
        IMessage request,
        MessageParser<TResponse> responseParser,
        CancellationToken cancellationToken)
        where TResponse : class, IMessage<TResponse>, new()
    {
        var result = await RequestResultAsync(request, responseParser, cancellationToken);
        if (result.HasResponse) return result.Response!;
        throw new GameProtocolException(result.ErrorCode, result.ErrorMessage, string.Empty);
    }

    public async Task<GameRequestResult<TResponse>> RequestResultAsync<TResponse>(
        IMessage request,
        MessageParser<TResponse> responseParser,
        CancellationToken cancellationToken)
        where TResponse : class, IMessage<TResponse>, new()
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(responseParser);
        var envelope = await RequestRawAsync(
            request.Descriptor.FullName,
            request.ToByteArray(),
            cancellationToken);
        TResponse? parsed = null;
        try { parsed = responseParser.ParseFrom(envelope.Payload); }
        catch (InvalidProtocolBufferException) { parsed = null; }

        if (envelope.ErrorCode == 0)
            return new GameRequestResult<TResponse>
            {
                Success = true,
                ErrorCode = 0,
                ErrorMessage = string.Empty,
                Response = parsed ?? throw new InvalidDataException("响应载荷无法解析。")
            };

        return new GameRequestResult<TResponse>
        {
            Success = false,
            ErrorCode = envelope.ErrorCode,
            ErrorMessage = envelope.ErrorMessage,
            Response = parsed
        };
    }

    public async Task<envelope> RequestRawAsync(
        string messageType,
        byte[] payload,
        CancellationToken cancellationToken) =>
        await SendRequestAsync(messageType, payload, allowAuthRetry: true, cancellationToken);

    public void ApplyAccessToken(AccessTokenGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentException.ThrowIfNullOrWhiteSpace(grant.AccessToken);
        AccessToken = grant.AccessToken;
        _accessTokenGrant = grant;
    }

    public void ClearAccessToken()
    {
        AccessToken = string.Empty;
        _accessTokenGrant = null;
    }

    public IDisposable On<T>(MessageParser<T> parser, Action<T> handler)
        where T : class, IMessage<T>, new()
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(handler);
        var typeName = new T().Descriptor.FullName;
        Action<envelope> wrapped = envelope =>
        {
            try { handler(parser.ParseFrom(envelope.Payload)); }
            catch (InvalidProtocolBufferException)
            {
                // 推送类型对但载荷损坏时跳过该订阅方。
            }
        };
        var list = _typedHandlers.GetOrAdd(typeName, _ => []);
        lock (list) list.Add(wrapped);
        return new Subscription(() =>
        {
            lock (list) list.Remove(wrapped);
        });
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        _disconnectRequested = true;
        var lifetime = _lifetime;
        _lifetime = null;
        if (lifetime is not null) await lifetime.CancelAsync();
        await _transport.DisconnectAsync(cancellationToken);
        FailPending(new IOException("客户端已断开。"));
        lifetime?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync(CancellationToken.None);
        await _transport.DisposeAsync();
        _connectGate.Dispose();
        _tokenRefreshGate.Dispose();
    }

    private async Task<envelope> SendRequestAsync(
        string messageType,
        byte[] payload,
        bool allowAuthRetry,
        CancellationToken cancellationToken)
    {
        if (_tokenRefreshDepth == 0)
            await TryRefreshAccessTokenAsync(force: false, cancellationToken);

        var response = await SendOnceAsync(messageType, payload, cancellationToken);
        if (allowAuthRetry
            && response.ErrorCode == ProtocolError.AuthenticationRequired
            && TokenRefreshHandler is not null
            && _tokenRefreshDepth == 0
            && await TryRefreshAccessTokenAsync(force: true, cancellationToken))
        {
            return await SendOnceAsync(messageType, payload, cancellationToken);
        }

        return response;
    }

    private async Task<envelope> SendOnceAsync(
        string messageType,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        if (!_transport.IsConnected) throw new InvalidOperationException("尚未连接。");
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        var requestId = Guid.NewGuid().ToString("N");
        var request = new envelope
        {
            Type = messageType,
            Payload = Google.Protobuf.ByteString.CopyFrom(payload ?? []),
            Token = AccessToken,
            RequestId = requestId,
            Version = ClientVersion,
            Channel = CommunicationChannel.Client
        };
        var completion = new TaskCompletionSource<envelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(requestId, completion))
            throw new InvalidOperationException("请求 ID 重复。");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        using var register = timeout.Token.Register(() =>
        {
            if (_pending.TryRemove(requestId, out var removed))
                removed.TrySetCanceled();
        });
        try
        {
            await _transport.SendAsync(request.ToByteArray(), timeout.Token);
            return await completion.Task;
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    private async Task<bool> TryRefreshAccessTokenAsync(bool force, CancellationToken cancellationToken)
    {
        if (TokenRefreshHandler is null) return false;
        if (!force && !IsAccessTokenExpiringSoon()) return false;

        await _tokenRefreshGate.WaitAsync(cancellationToken);
        try
        {
            if (!force && !IsAccessTokenExpiringSoon()) return false;
            Interlocked.Increment(ref _tokenRefreshDepth);
            try
            {
                var grant = await TokenRefreshHandler(cancellationToken);
                if (grant is null || string.IsNullOrWhiteSpace(grant.AccessToken))
                    return false;
                ApplyAccessToken(grant);
                return true;
            }
            finally
            {
                Interlocked.Decrement(ref _tokenRefreshDepth);
            }
        }
        finally
        {
            _tokenRefreshGate.Release();
        }
    }

    private bool IsAccessTokenExpiringSoon()
    {
        var expireUnix = _accessTokenGrant?.ExpireUnix ?? 0;
        if (expireUnix <= 0
            && TokenSigner.TryReadToken(AccessToken, out _, out _, out var parsedExpire))
            expireUnix = parsedExpire;
        if (expireUnix <= 0) return false;

        var lead = _accessTokenGrant is { RecommendedRefreshSeconds: > 0 }
            ? _accessTokenGrant.RecommendedRefreshSeconds
            : Math.Max(1, (int)TokenRefreshLeadTime.TotalSeconds);
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expireUnix - lead;
    }

    private async Task<ServiceEndpoint> ConnectCoreAsync(ServiceQuery query, CancellationToken cancellationToken)
    {
        await _connectGate.WaitAsync(cancellationToken);
        try
        {
            if (_transport.IsConnected && _transport.CurrentEndpoint is { } current)
                return current;

            var endpoints = await _discovery.ResolveAsync(query ?? new ServiceQuery(), cancellationToken);
            var ordered = Order(endpoints, query?.RoutingKey);
            if (ordered.Count == 0)
                throw new InvalidOperationException("服务发现没有返回可用节点。");

            Exception? last = null;
            foreach (var endpoint in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (RequireCompatibleVersion
                    && !ProtocolCompatibility.AreCompatible(ClientVersion, endpoint.Version))
                {
                    last = new GameProtocolException(
                        ProtocolError.VersionMismatch,
                        $"协议版本不匹配，客户端 {ClientVersion}，服务器 {endpoint.Version}",
                        string.Empty);
                    continue;
                }

                try
                {
                    await _transport.ConnectAsync(endpoint, cancellationToken);
                    StartReceiveLoop();
                    StartHeartbeat(endpoint);
                    return endpoint;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    last = exception;
                }
            }

            throw new InvalidOperationException("所有发现节点都无法连接。", last);
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private void StartReceiveLoop()
    {
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = new CancellationTokenSource();
        _ = ReceiveLoopAsync(_lifetime.Token);
    }

    private void StartHeartbeat(ServiceEndpoint endpoint)
    {
        if (!AutoHeartbeat || endpoint.HeartbeatIntervalSeconds <= 0 || _lifetime is null)
            return;
        _ = HeartbeatLoopAsync(endpoint.HeartbeatIntervalSeconds, _lifetime.Token);
    }

    private async Task HeartbeatLoopAsync(int intervalSeconds, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, intervalSeconds)));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var request = new ClientHeartbeatRequest
                {
                    ClientTimeUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };
                var response = await RequestRawAsync(
                    ClientHeartbeatRequest.Descriptor.FullName,
                    request.ToByteArray(),
                    cancellationToken);
                if (response.ErrorCode == ProtocolError.UnknownMessageType)
                    return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            System.Diagnostics.Debug.WriteLine("heartbeat canceled");
        }
        catch (Exception)
        {
            // 下一次周期再试；连接失败由收包循环处理。
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var bytes = await _transport.ReceiveAsync(cancellationToken);
                var message = envelope.Parser.ParseFrom(bytes);
                if (!string.IsNullOrEmpty(message.RequestId)
                    && _pending.TryRemove(message.RequestId, out var completion))
                {
                    completion.TrySetResult(message);
                    continue;
                }

                DispatchNotification(message);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            System.Diagnostics.Debug.WriteLine("receive loop canceled");
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            if (failure is not null)
            {
                FailPending(failure);
                try { await _transport.DisconnectAsync(CancellationToken.None); }
                catch { /* 清理 */ }
                ConnectionClosed?.Invoke(failure);
                _ = TryReconnectAsync();
            }
        }
    }

    private void DispatchNotification(envelope message)
    {
        if (_typedHandlers.TryGetValue(message.Type, out var handlers))
        {
            Action<envelope>[] snapshot;
            lock (handlers) snapshot = [.. handlers];
            foreach (var handler in snapshot)
            {
                try { handler(message); }
                catch (Exception)
                {
                    // 订阅方异常不影响收包。
                }
            }
        }

        NotificationReceived?.Invoke(message);
    }

    private async Task TryReconnectAsync()
    {
        if (_disconnectRequested || !AutoReconnect || _lastQuery is null) return;
        if (Interlocked.CompareExchange(ref _reconnecting, 1, 0) != 0) return;
        try
        {
            for (var attempt = 0; attempt < Math.Max(1, MaxReconnectAttempts); attempt++)
            {
                if (_disconnectRequested) return;
                await Task.Delay(ReconnectDelay);
                try
                {
                    await ConnectCoreAsync(_lastQuery, CancellationToken.None);
                    Reconnected?.Invoke();
                    return;
                }
                catch
                {
                    // 继续下一次
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _reconnecting, 0);
        }
    }

    private void FailPending(Exception exception)
    {
        foreach (var item in _pending.ToArray())
        {
            if (_pending.TryRemove(item.Key, out var completion))
                completion.TrySetException(exception);
        }
    }

    private static IReadOnlyList<ServiceEndpoint> Order(IReadOnlyList<ServiceEndpoint> endpoints, string? routingKey)
    {
        var ordered = endpoints.OrderBy(item => item.Load).ThenBy(item => item.ServiceId, StringComparer.Ordinal).ToArray();
        if (string.IsNullOrEmpty(routingKey) || ordered.Length <= 1) return ordered;
        unchecked
        {
            uint hash = 2166136261;
            foreach (var ch in routingKey)
                hash = (hash ^ ch) * 16777619;
            var start = (int)(hash % (uint)ordered.Length);
            return [.. ordered.Skip(start), .. ordered.Take(start)];
        }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
