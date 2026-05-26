using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using DotNetGameServer;
using Google.Protobuf;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using R3;
using Core.Telemetry;
using Core.Logging;
using Core.Components;
using Core.MessagePipeline;
using Shared.Time;
using Core.Configuration;

namespace Core;

public abstract class BaseGameServer : IServerContext
{
    private readonly int _maxMessageSize;
    private readonly int _maxConcurrentConnections;
    private readonly int _receiveTimeoutMs;
    private readonly int _acceptBackoffMs;
    private int _currentConnections;
    private TcpListener? _listener;

    private CancellationTokenSource? _cts;

    // 请求处理管线：遥测 -> 鉴权 -> 处理器分发。
    private MessageDelegate? _pipeline;

    private readonly ILogger _logger;
    private readonly bool _forwardToILogger;

    // 连接数指标通过 Meter 注册，无需持有字段引用。
    private readonly ConcurrentDictionary<string, IHandlerInvoker> _typedHandlers = new();

    private readonly ConcurrentDictionary<Type, IServerComponent> _components = new();

    // 网络鲁棒性：限速与黑名单
    private readonly int _maxBytesPerSecond; // 默认每连接 1MB/s
    private readonly int _maxMessagesPerSecond; // 默认每连接 200 条/秒
    private readonly int _idleTimeoutMs; // 默认空闲 120s 断开

    private readonly ConcurrentDictionary<string, DateTime> _blacklist = new();

    // 聚合的滑动窗口限流
    private readonly ConcurrentDictionary<string, IpSlidingRate> _ipRates = new();
    private readonly int _ipMaxBytesPerSecond; // 0 表示禁用
    private readonly int _ipMaxMessagesPerSecond; // 0 表示禁用

    private int _rateEventCount;
    private IDisposable? _rateSub;

    /// <summary>
    /// 服务器名称，用于日志前缀与标识。
    /// </summary>
    public string ServerName { get; }

    // 基类级默认上下文字段：用于非请求场景（初始化等）。
    public string AccessToken => string.Empty;
    public string RemoteAddress => "";
    public string RequestId => "";
    public AuthInfo? Auth => null;

    /// <summary>
    /// 当前监听端口；未启动或已停止时为 <c>null</c>。
    /// </summary>
    public int? ListeningPort { get; private set; }
    public bool IsRunning => _listener != null;

    protected BaseGameServer(string serverName, ILogger? logger, ServerOptions? options = null, string? tokenSecret = null, bool forwardToILogger = false)
    {
        ServerName = serverName;
        _logger = logger ?? new DelegateLogger(Console.WriteLine, serverName);
        _forwardToILogger = forwardToILogger;
        var settings = options ?? new ServerOptions();
        _maxMessageSize = Math.Max(1024, settings.MaxMessageSizeBytes);
        _maxConcurrentConnections = Math.Max(1, settings.MaxConcurrentConnections);
        _receiveTimeoutMs = Math.Max(0, settings.ReceiveTimeoutMs);
        _acceptBackoffMs = Math.Max(0, settings.AcceptBackoffMs);
        _maxBytesPerSecond = Math.Max(1, settings.MaxBytesPerSecond);
        _maxMessagesPerSecond = Math.Max(1, settings.MaxMessagesPerSecond);
        _idleTimeoutMs = Math.Max(0, settings.IdleTimeoutMs);
        _ipMaxBytesPerSecond = Math.Max(0, settings.IpMaxBytesPerSecond);
        _ipMaxMessagesPerSecond = Math.Max(0, settings.IpMaxMessagesPerSecond);
        CoreTelemetry.CreateConnectionsGauge(() => _currentConnections, ServerName);

        try
        {
            AddComponent<ITokenSecretProvider>(new TokenSecretProviderComponent(tokenSecret));
            AddComponent<ITokenRevocationStore>(new InMemoryTokenRevocationStore());
        }
        catch (Exception ex)
        {
            Log($"初始化 TokenSecretProviderComponent 失败: {ex.Message}");
        }
    }

    public void AddComponent<T>(T component) where T : class, IServerComponent
    {
        _components[typeof(T)] = component;
    }

    /// <summary>
    /// 获取已注册的服务器组件。
    /// </summary>
    /// <typeparam name="T">组件接口或具体类型。</typeparam>
    /// <returns>若找到则返回组件实例，否则返回 <c>null</c>。</returns>
    public T? GetComponent<T>() where T : class, IServerComponent
    {
        return _components.TryGetValue(typeof(T), out var component) ? component as T : null;
    }


    /// <summary>
    /// 注册强类型消息处理器，采用具体的请求/响应类型进行分发。
    /// 同时注册短名与全名以兼容不同的 <c>envelope.type</c> 表示。
    /// </summary>
    /// <typeparam name="TReq">请求消息类型。</typeparam>
    /// <typeparam name="TResp">响应消息类型。</typeparam>
    /// <param name="handler">强类型消息处理器。</param>
    protected void RegisterMessageHandler<TReq, TResp>(IMessageHandler<TReq, TResp> handler)
        where TReq : class, IMessage<TReq>, new()
        where TResp : class, IMessage<TResp>, new()
    {
        var invoker = new HandlerInvoker<TReq, TResp>(handler);
        var reqName = new TReq().Descriptor.Name;
        var reqFullName = new TReq().Descriptor.FullName;
        _typedHandlers[reqName] = invoker;
        _typedHandlers[reqFullName] = invoker;
    }

    /// <summary>
    /// 启动服务器监听并初始化所有已注册组件。
    /// </summary>
    /// <param name="port">监听端口。</param>
    public virtual async Task StartAsync(int port)
    {
        if (_listener != null) return;

        // 逐个初始化组件，确保依赖就绪
        foreach (var component in _components.Values)
        {
            await component.InitializeAsync();
        }

        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        ListeningPort = port;

        // 构建请求处理管线（健康检查端点由宿主统一提供，基类不再内嵌 Web 服务器）。
        _pipeline = new MessagePipelineBuilder()
            .UseTelemetry()
            .UseAuth()
            .UseHandler()
            .Build();

        Log($"服务器已启动，端口 {port}");

        _ = Task.Run(() => AcceptLoop(_cts.Token));
        _rateSub = Observable.Interval(TimeSpan.FromSeconds(1)).Subscribe(_ =>
        {
            var c = Interlocked.Exchange(ref _rateEventCount, 0);
            if (c > 0) Log($"限流事件: {c}");
        });
    }

    /// <summary>
    /// 停止服务器监听并销毁所有组件。
    /// </summary>
    public async Task StopAsync()
    {
        try
        {
            await _cts?.CancelAsync()!;
        }
        catch (Exception ex)
        {
            // 取消令牌可能已释放或已取消，记录后继续关停流程。
            Log($"取消服务器运行令牌失败: {ex.Message}");
        }

        try
        {
            _listener?.Stop();
        }
        catch (Exception ex)
        {
            // 监听器停止失败不影响后续资源释放，记录后继续。
            Log($"停止 TCP 监听器失败: {ex.Message}");
        }

        _listener = null;
        ListeningPort = null;
        _pipeline = null;

        try { _rateSub?.Dispose(); }
        catch
        {
            // 释放订阅失败可忽略：不影响关闭流程
        }
        _rateSub = null;

        // 逐个销毁组件，释放资源
        foreach (var component in _components.Values)
        {
            await component.DestroyAsync();
        }

        Log("服务器已停止");
    }

    /// <summary>
    /// 写入一条日志，自动加上服务器名称前缀。
    /// </summary>
    /// <param name="message">日志消息。</param>
    public void Log(string message)
    {
        LogWithCategory(message, ServerName, LogLevel.Information);
    }

    /// <summary>
    /// 写入一条日志，自动加上服务器名称前缀。
    /// </summary>
    /// <param name="message">日志消息。</param>
    /// <param name="level">日志级别。</param>
    public void Log(string message, LogLevel level)
    {
        LogWithCategory(message, ServerName, level);
    }

    public void LogHeartbeat(string message)
    {
        LogWithCategory(message, "Heartbeat", LogLevel.Information);
    }

    public void LogWithCategory(string message, string category, LogLevel level)
    {
        LogWithContext(message, category, level);
    }

    internal void LogWithContext(
        string message,
        string category,
        LogLevel level,
        string? remote = null,
        string? requestId = null,
        string? userId = null)
    {
        if (LogCenter.ShouldLog(level, category))
        {
            if (_forwardToILogger)
                _logger.Log(level, "[{Server}] {Message}", ServerName, message);
            LogCenter.Publish(new LogEvent
            {
                Level = level,
                Category = category,
                Message = message,
                Server = ServerName,
                Remote = string.IsNullOrWhiteSpace(remote) ? null : remote,
                RequestId = string.IsNullOrWhiteSpace(requestId) ? null : requestId,
                UserId = string.IsNullOrWhiteSpace(userId) ? null : userId
            });
        }
    }

    private async Task AcceptLoop(CancellationToken token)
    {
        var currentBackoffMs = _acceptBackoffMs;
        while (!token.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(token);
                // 黑名单：立即拒绝
                var remoteEp = client.Client.RemoteEndPoint?.ToString() ?? "未知";
                if (IsBlacklisted(remoteEp))
                {
                    try
                    {
                        Log($"拒绝黑名单连接: {remoteEp}");
                        client.Close();
                    }
                    catch (Exception ex)
                    {
                        // 关闭黑名单连接失败不影响服务稳定性，记录后继续。
                        Log($"关闭黑名单连接失败: {ex.Message}");
                    }

                    // 成功接受一次连接，重置退避
                    currentBackoffMs = _acceptBackoffMs;
                    continue;
                }

                var connCount = Volatile.Read(ref _currentConnections);
                // 连接数限制：超过并发上限直接拒绝
                if (connCount >= _maxConcurrentConnections)
                {
                    try
                    {
                        Log($"接受到连接但已达上限 {_maxConcurrentConnections}，拒绝 {client.Client.RemoteEndPoint}");
                        client.Close();
                    }
                    catch (Exception ex)
                    {
                        // 关闭超限连接失败不影响服务稳定性，记录后继续。
                        Log($"关闭超限连接失败: {ex.Message}");
                    }

                    continue;
                }

                _ = Task.Run(() => HandleClient(client, token), token);
                // 成功接受连接后重置退避
                currentBackoffMs = _acceptBackoffMs;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log($"接受连接异常: {ex.Message}，将稍后重试");
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(currentBackoffMs), token);
                    // 指数退避，上限 10s
                    currentBackoffMs = Math.Min(currentBackoffMs <= 0 ? _acceptBackoffMs : currentBackoffMs * 2,
                        10_000);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// 从网络流精确读取指定长度的字节；若对端关闭则返回已读取长度。
    /// </summary>
    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int offset, int count,
        CancellationToken token, int timeoutMs)
    {
        var total = 0;
        while (total < count)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            if (timeoutMs > 0) cts.CancelAfter(timeoutMs);
            var n = await stream.ReadAsync(buffer.AsMemory(offset + total, count - total), cts.Token);
            if (n == 0) return total;
            total += n;
        }

        return total;
    }

    /// <summary>
    /// 将响应类型与载荷封装为信封并写入网络流。
    /// </summary>
    private static async Task WriteEnvelopeAsync(Stream stream, string type, byte[] payload, string? requestId,
        int errorCode, string? errorMessage, CancellationToken token, int timeoutMs)
    {
        var env = new envelope { Type = type, Payload = ByteString.CopyFrom(payload) };
        if (!string.IsNullOrWhiteSpace(requestId)) env.RequestId = requestId;
        // 服务器不再回写协议版本字段，简化并避免客户端版本依赖
        env.ErrorCode = errorCode;
        if (!string.IsNullOrWhiteSpace(errorMessage)) env.ErrorMessage = errorMessage;
        var bytes = env.ToByteArray();
        var lenBuf = BitConverter.GetBytes(bytes.Length);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (timeoutMs > 0) cts.CancelAfter(timeoutMs);
        await stream.WriteAsync(lenBuf.AsMemory(0, 4), cts.Token);
        await stream.WriteAsync(bytes.AsMemory(), cts.Token);
        await stream.FlushAsync(cts.Token);
    }

    private async Task HandleClient(TcpClient client, CancellationToken token)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "未知";
        var remoteIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address?.ToString() ?? remote;
        Log($"客户端连接: {remote}");
        using (client)
        {
            Interlocked.Increment(ref _currentConnections);
            var stream = client.GetStream();
            try
            {
                try { client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true); }
                catch
                {
                    // 某些平台不支持或设置失败，忽略不影响功能
                }
                client.ReceiveTimeout = _idleTimeoutMs;
                client.SendTimeout = _receiveTimeoutMs;
            }
            catch (Exception ex)
            {
                Log($"设置接收/发送超时失败: {ex.Message}");
            }

            const int bucketCount = 10;
            const int bucketMs = 100;
            int currentBucketIndex = 0;
            long bucketWindowStartTicks = Stopwatch.GetTimestamp();
            int[] messageBuckets = new int[bucketCount];
            int[] byteBuckets = new int[bucketCount];

            void AdvanceBucketsIfNeeded()
            {
                var elapsedMs = (Stopwatch.GetTimestamp() - bucketWindowStartTicks) * 1000.0 / Stopwatch.Frequency;
                if (elapsedMs >= bucketMs)
                {
                    var steps = (int)(elapsedMs / bucketMs);
                    for (var i = 0; i < Math.Min(steps, bucketCount); i++)
                    {
                        currentBucketIndex = (currentBucketIndex + 1) % bucketCount;
                        messageBuckets[currentBucketIndex] = 0;
                        byteBuckets[currentBucketIndex] = 0;
                    }
                    bucketWindowStartTicks = Stopwatch.GetTimestamp();
                }
            }

            int AddBytesAndGetRate(int bytes)
            {
                AdvanceBucketsIfNeeded();
                byteBuckets[currentBucketIndex] += bytes;
                var sum = 0;
                for (var i = 0; i < bucketCount; i++) sum += byteBuckets[i];
                return sum;
            }

            int AddMessagesAndGetRate(int inc)
            {
                AdvanceBucketsIfNeeded();
                messageBuckets[currentBucketIndex] += inc;
                var sum = 0;
                for (var i = 0; i < bucketCount; i++) sum += messageBuckets[i];
                return sum;
            }

            bool CheckByteRateLimit(int bytes, string remoteEndpoint, string remoteIpOnly)
            {
                string reason;
                var bps = AddBytesAndGetRate(bytes);
                if (_ipMaxBytesPerSecond > 0)
                {
                    var ipRate = _ipRates.GetOrAdd(remoteIpOnly, _ => new IpSlidingRate());
                    var ipBpsNow = ipRate.AddBytesAndGetRate(bytes);
                    if (ipBpsNow > _ipMaxBytesPerSecond)
                    {
                        reason = "ip_bytes_rate_limit_exceeded";
                        Log($"[IP限速-字节] {remoteEndpoint} {ipBpsNow}B/s > {_ipMaxBytesPerSecond}B/s，断开");
                        Interlocked.Increment(ref _rateEventCount);
                        AddToBlacklist(remoteIpOnly, TimeSpan.FromMinutes(2), reason);
                        return false;
                    }
                }
                if (bps > _maxBytesPerSecond)
                {
                    reason = "bytes_rate_limit_exceeded";
                    Log($"限速超限(字节): {remoteEndpoint} {bps}B/s > {_maxBytesPerSecond}B/s，断开");
                    Interlocked.Increment(ref _rateEventCount);
                    AddToBlacklist(remoteIpOnly, TimeSpan.FromMinutes(2), reason);
                    return false;
                }
                return true;
            }

            bool CheckMessageRateLimit(string remoteEndpoint, string remoteIpOnly)
            {
                string reason;
                var mps = AddMessagesAndGetRate(1);
                if (_ipMaxMessagesPerSecond > 0)
                {
                    var ipRate = _ipRates.GetOrAdd(remoteIpOnly, _ => new IpSlidingRate());
                    var ipMpsNow = ipRate.AddMessagesAndGetRate(1);
                    if (ipMpsNow > _ipMaxMessagesPerSecond)
                    {
                        reason = "ip_message_rate_limit_exceeded";
                        Log($"[IP限速-频率] {remoteEndpoint} {ipMpsNow}/s > {_ipMaxMessagesPerSecond}/s，断开");
                        Interlocked.Increment(ref _rateEventCount);
                        AddToBlacklist(remoteIpOnly, TimeSpan.FromMinutes(2), reason);
                        return false;
                    }
                }
                if (mps > _maxMessagesPerSecond)
                {
                    reason = "message_rate_limit_exceeded";
                    Log($"限速超限(频率): {remoteEndpoint} {mps}/s > {_maxMessagesPerSecond}/s，断开");
                    Interlocked.Increment(ref _rateEventCount);
                    AddToBlacklist(remoteIpOnly, TimeSpan.FromMinutes(2), reason);
                    return false;
                }
                return true;
            }

            var requestChannel = Channel.CreateUnbounded<envelope>(new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = false,
                SingleReader = true,
                SingleWriter = true
            });

            var requestProcessor = Task.Run(async () =>
            {
                await foreach (var env in requestChannel.Reader.ReadAllAsync(token))
                {
                    try
                    {
                        var incomingReqId = string.IsNullOrWhiteSpace(env.RequestId)
                            ? Guid.NewGuid().ToString("N")
                            : env.RequestId;
                        Log($"recv route={env.Type} req_id={incomingReqId} ip={remote}");

                        var msgContext = new MessageContext
                        {
                            Envelope = env,
                            Server = this,
                            RemoteAddress = remote,
                            RequestId = incomingReqId,
                            Token = token
                        };

                        await (_pipeline?.Invoke(msgContext) ?? Task.CompletedTask);

                        if (msgContext.HasResponse)
                        {
                            await WriteEnvelopeAsync(stream, msgContext.ResponseType!, msgContext.ResponsePayload ?? [],
                                env.RequestId, msgContext.ResponseErrorCode, msgContext.ResponseErrorMessage, token,
                                _receiveTimeoutMs);
                            Log($"send route={msgContext.ResponseType} req_id={env.RequestId} ip={remote} error_code={msgContext.ResponseErrorCode}");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception e)
                    {
                        Log(e.Message);
                    }
                }
            }, token);

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var lenBuf = new byte[4];
                    var read = await ReadExactAsync(stream, lenBuf, 0, 4, token, _receiveTimeoutMs);
                    if (read == 0) break;
                    if (!CheckByteRateLimit(read, remote, remoteIp)) break;
                    var length = BitConverter.ToInt32(lenBuf, 0);
                    if (length <= 0 || length > _maxMessageSize)
                    {
                        Log($"非法或过大消息长度: {length}，断开连接");
                        break;
                    }
                    var buf = new byte[length];
                    await ReadExactAsync(stream, buf, 0, length, token, _receiveTimeoutMs);
                    if (!CheckByteRateLimit(length, remote, remoteIp)) break;
                    envelope envelope;
                    try
                    {
                        envelope = envelope.Parser.ParseFrom(buf);
                    }
                    catch (InvalidProtocolBufferException exception)
                    {
                        Log($"协议解析异常(envelope): {exception.Message}，断开");
                        break;
                    }
                    if (!CheckMessageRateLimit(remote, remoteIp)) break;
                    if (!requestChannel.Writer.TryWrite(envelope))
                    {
                        Log($"请求队列写入失败: {remote} route={envelope.Type}");
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Log($"客户端请求被取消: {remote}");
            }
            catch (IOException ioException)
            {
                Log($"客户端网络IO异常: {remote} {ioException.Message}");
            }
            catch (SocketException sex)
            {
                Log($"客户端套接字异常: {remote} {sex.Message}");
            }
            catch (Exception ex)
            {
                Log($"客户端异常: {ex.Message}");
            }

            requestChannel.Writer.TryComplete();
            try { await requestProcessor; }
            catch (Exception ex)
            {
                Log($"请求处理器异常: {ex.Message}");
            }
            Log($"客户端断开: {remote}");
            Interlocked.Decrement(ref _currentConnections);
        }
    }

    private bool IsBlacklisted(string remote)
    {
        // 允许 remote 为 "IP:port"；按原样匹配并清理过期项
        if (_blacklist.TryGetValue(remote, out var until))
        {
            if (TimeManager.UtcNow < until) return true;
            _blacklist.TryRemove(remote, out _);
        }

        return false;
    }

    private void AddToBlacklist(string remote, TimeSpan duration, string reason)
    {
        _blacklist[remote] = TimeManager.UtcNow.Add(duration);
        Log($"加入黑名单: {remote} 原因={reason} 时长={duration}");
    }

    public IReadOnlyDictionary<string, DateTime> ListBlacklist()
    {
        return new Dictionary<string, DateTime>(_blacklist);
    }

    public void BlacklistAdd(string remote, TimeSpan duration, string reason)
    {
        AddToBlacklist(remote, duration, reason);
    }

    public bool BlacklistRemove(string remote)
    {
        return _blacklist.TryRemove(remote, out _);
    }

    public string[] ListHandlerNames()
    {
        return _typedHandlers.Keys.ToArray();
    }

    internal IReadOnlyDictionary<string, IHandlerInvoker> ListHandlerInvokers()
    {
        return _typedHandlers;
    }

    public object GetLimitsSnapshot()
    {
        return new
        {
            MaxMessageSizeBytes = _maxMessageSize,
            MaxConcurrentConnections = _maxConcurrentConnections,
            ReceiveTimeoutMs = _receiveTimeoutMs,
            AcceptBackoffMs = _acceptBackoffMs,
            MaxBytesPerSecond = _maxBytesPerSecond,
            MaxMessagesPerSecond = _maxMessagesPerSecond,
            IdleTimeoutMs = _idleTimeoutMs,
            IpMaxBytesPerSecond = _ipMaxBytesPerSecond,
            IpMaxMessagesPerSecond = _ipMaxMessagesPerSecond
        };
    }

    public object GetRuntimeSnapshot()
    {
        return new
        {
            ServerName,
            ListeningPort,
            CurrentConnections = Volatile.Read(ref _currentConnections),
            BlacklistCount = _blacklist.Count
        };
    }

    /// <summary>
    /// IP 滑动窗口限流器
    /// </summary>
    private sealed class IpSlidingRate
    {
        private const int BucketCount = 10;
        private const int BucketMs = 100;
        private int _currentBucketIndex;
        private long _bucketWindowStartTicks = Stopwatch.GetTimestamp();
        private readonly int[] _messageBuckets = new int[BucketCount];
        private readonly int[] _byteBuckets = new int[BucketCount];

        // 检查并更新滑动窗口
        private void AdvanceBucketsIfNeeded()
        {
            var elapsedMs = (Stopwatch.GetTimestamp() - _bucketWindowStartTicks) * 1000.0 / Stopwatch.Frequency;
            if (elapsedMs >= BucketMs)
            {
                var steps = (int)(elapsedMs / BucketMs);
                for (var i = 0; i < Math.Min(steps, BucketCount); i++)
                {
                    _currentBucketIndex = (_currentBucketIndex + 1) % BucketCount;
                    _messageBuckets[_currentBucketIndex] = 0;
                    _byteBuckets[_currentBucketIndex] = 0;
                }

                _bucketWindowStartTicks = Stopwatch.GetTimestamp();
            }
        }

        // 添加字节
        public int AddBytesAndGetRate(int bytes)
        {
            AdvanceBucketsIfNeeded();
            _byteBuckets[_currentBucketIndex] += bytes;
            var sum = 0;
            for (var i = 0; i < BucketCount; i++) sum += _byteBuckets[i];
            return sum;
        }

        // 添加消息
        public int AddMessagesAndGetRate(int inc)
        {
            AdvanceBucketsIfNeeded();
            _messageBuckets[_currentBucketIndex] += inc;
            var sum = 0;
            for (var i = 0; i < BucketCount; i++) sum += _messageBuckets[i];
            return sum;
        }
    }
}

