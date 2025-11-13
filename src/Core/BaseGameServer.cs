using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using DotNetGameServer;
using Google.Protobuf;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using R3;
using Core.Telemetry;
using Core.Logging;
using Core.Components;

namespace Core;

/// <summary>
/// 基础游戏服务器类，采用ECS架构
/// </summary>
public abstract class BaseGameServer : IServerContext
{
    private readonly int _maxMessageSize; // 可配置消息长度上限
    private readonly int _maxConcurrentConnections; // 可配置连接上限
    private readonly int _receiveTimeoutMs; // 收发超时（软限制）
    private readonly int _acceptBackoffMs; // 接受失败退避毫秒
    private int _currentConnections;
    private TcpListener? _listener;

    private CancellationTokenSource? _cts; // 用于取消监听

    // ECS 更新循环通过后台 Task 驱动，无需持有字段引用。
    private IWebHost? _webHost; // 用于后台监控

    private readonly ILogger _logger; // 日志

    // 连接数指标通过 Meter 注册，无需持有字段引用。
    private readonly ConcurrentDictionary<string, IStrongHandlerInvoker> _typedHandlers = new();

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
    private IDisposable? _ecsSubscription;
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
    protected int? ListeningPort { get; private set; }

    protected BaseGameServer(string serverName, ILogger? logger)
    {
        ServerName = serverName;
        _logger = logger ?? new DelegateLogger(Console.WriteLine, serverName);
        var settings = Config.SimpleConfigLoader.LoadServerSettings(AppContext.BaseDirectory);
        _maxMessageSize = Math.Max(1024, settings.MaxMessageSizeBytes);
        _maxConcurrentConnections = Math.Max(1, settings.MaxConcurrentConnections);
        _receiveTimeoutMs = Math.Max(0, settings.ReceiveTimeoutMs);
        _acceptBackoffMs = Math.Max(0, settings.AcceptBackoffMs);
        CoreTelemetry.CreateConnectionsGauge(() => _currentConnections, ServerName);
        // 设定默认网络限速与空闲超时
        _maxBytesPerSecond = 1_000_000;
        _maxMessagesPerSecond = 200;
        _idleTimeoutMs = 120_000;
        // 若后续提供配置，可在此覆盖默认值（环境变量或配置）
        if (int.TryParse(Environment.GetEnvironmentVariable(Shared.Constants.ConfigKeys.ServerMaxBpsEnv),
                out var bps) && bps > 0)
            _maxBytesPerSecond = bps;
        if (int.TryParse(Environment.GetEnvironmentVariable(Shared.Constants.ConfigKeys.ServerMaxMsgsEnv),
                out var mps) && mps > 0)
            _maxMessagesPerSecond = mps;
        if (int.TryParse(Environment.GetEnvironmentVariable(Shared.Constants.ConfigKeys.ServerIdleMsEnv),
                out var idle) && idle > 0)
            _idleTimeoutMs = idle;
        // 按 IP 的聚合限流阈值（可选）：未设置或 <=0 则禁用
        _ipMaxBytesPerSecond =
            int.TryParse(Environment.GetEnvironmentVariable("SERVER_IP_MAX_BPS"), out var ipBps) && ipBps > 0
                ? ipBps
                : 0;
        _ipMaxMessagesPerSecond =
            int.TryParse(Environment.GetEnvironmentVariable("SERVER_IP_MAX_MSGS"), out var ipMps) && ipMps > 0
                ? ipMps
                : 0;

        // 集中密钥提供组件：统一加载并缓存鉴权密钥
        try
        {
            AddComponent<ITokenSecretProvider>(new TokenSecretProviderComponent(AppContext.BaseDirectory));
            // 令牌撤销列表（内存实现）。如需分布式，可在具体服务中替换为 Redis 实现。
            AddComponent<ITokenRevocationStore>(new InMemoryTokenRevocationStore());
        }
        catch (Exception ex)
        {
            // 鉴权密钥提供组件初始化失败不影响服务器启动（仅影响需要认证的场景），记录后继续。
            Log($"初始化 TokenSecretProviderComponent 失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 添加服务器组件并按类型注册，供运行期检索与使用。
    /// </summary>
    /// <typeparam name="T">组件接口或具体类型。</typeparam>
    /// <param name="component">组件实例，必须实现 <see cref="IServerComponent"/>。</param>
    protected void AddComponent<T>(T component) where T : class, IServerComponent
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
    /// 同时注册短名与全名以兼容不同的 <c>Envelope.type</c> 表示。
    /// </summary>
    /// <typeparam name="TReq">请求消息类型。</typeparam>
    /// <typeparam name="TResp">响应消息类型。</typeparam>
    /// <param name="handler">强类型消息处理器。</param>
    protected void RegisterStrongMessageHandler<TReq, TResp>(IStrongMessageHandler<TReq, TResp> handler)
        where TReq : class, IMessage<TReq>, new()
        where TResp : class, IMessage<TResp>, new()
    {
        var invoker = new StrongHandlerInvoker<TReq, TResp>(handler);
        // 同时注册短名与全名，兼容现有 Envelope.type 用法
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

        // 健康检查连接
        try
        {
            _webHost = new WebHostBuilder()
                .UseKestrel()
                .UseUrls($"http://*:{port + 10000}")
                .Configure(app =>
                {
                    app.Run(async context =>
                    {
                        if (context.Request.Path == "/health")
                        {
                            context.Response.StatusCode = 200;
                            await context.Response.WriteAsync("Healthy");
                        }
                    });
                })
                .Build();
            _ = _webHost.RunAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            Log($"健康检查端点启动失败: {ex.Message}");
        }

        Log($"服务器已启动，端口 {port}");

        _ = Task.Run(() => AcceptLoop(_cts.Token));
        var interval = TimeSpan.FromSeconds(1);
        _ecsSubscription = Observable.Interval(interval)
            .Subscribe(_ =>
            {
                try
                {
                    var ecs = GetComponent<Ecs.EcsWorldComponent>();
                    if (ecs != null)
                    {
                        Task.Run(() => ecs.World.UpdateAsync(interval));
                    }
                }
                catch (Exception ex)
                {
                    Log($"ECS 更新异常: {ex.Message}");
                }
            });
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

        try
        {
            _webHost?.StopAsync().Wait();
        }
        catch (Exception ex)
        {
            // WebHost 停止失败时继续执行组件销毁，避免阻塞关机。
            Log($"停止健康检查端点失败: {ex.Message}");
        }

        _listener = null;
        ListeningPort = null;

        try { _ecsSubscription?.Dispose(); }
        catch
        {
            // 释放订阅失败可忽略：不影响关闭流程
        }
        _ecsSubscription = null;
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
        if (LogCenter.ShouldLog(LogLevel.Information))
        {
            _logger.LogInformation("[{Server}] {Message}", ServerName, message);
            LogCenter.Publish(new LogEvent
            {
                Level = LogLevel.Information,
                Category = ServerName,
                Message = message,
                Server = ServerName
            });
        }
    }

    /// <summary>
    /// 写入一条日志，自动加上服务器名称前缀。
    /// </summary>
    /// <param name="message">日志消息。</param>
    /// <param name="level">日志级别。</param>
    public void Log(string message, LogLevel level)
    {
        if (LogCenter.ShouldLog(level))
        {
            _logger.Log(level, "[{Server}] {Message}", ServerName, message);
            LogCenter.Publish(new LogEvent
            {
                Level = level,
                Category = ServerName,
                Message = message,
                Server = ServerName
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

                _ = Task.Run(() => HandleClientR3(client, token), token);
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
        var env = new Envelope { Type = type, Payload = ByteString.CopyFrom(payload) };
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

    private async Task HandleClientR3(TcpClient client, CancellationToken token)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "未知";
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

            bool CheckByteRateLimit(int bytes, string remoteEndpoint)
            {
                string reason;
                var bps = AddBytesAndGetRate(bytes);
                if (_ipMaxBytesPerSecond > 0)
                {
                    var ipRate = _ipRates.GetOrAdd(remoteEndpoint, _ => new IpSlidingRate());
                    var ipBpsNow = ipRate.AddBytesAndGetRate(bytes);
                    if (ipBpsNow > _ipMaxBytesPerSecond)
                    {
                        reason = "ip_bytes_rate_limit_exceeded";
                        Log($"[IP限速-字节] {remoteEndpoint} {ipBpsNow}B/s > {_ipMaxBytesPerSecond}B/s，断开");
                        Interlocked.Increment(ref _rateEventCount);
                        AddToBlacklist(remoteEndpoint, TimeSpan.FromMinutes(2), reason);
                        return false;
                    }
                }
                if (bps > _maxBytesPerSecond)
                {
                    reason = "bytes_rate_limit_exceeded";
                    Log($"限速超限(字节): {remoteEndpoint} {bps}B/s > {_maxBytesPerSecond}B/s，断开");
                    Interlocked.Increment(ref _rateEventCount);
                    AddToBlacklist(remoteEndpoint, TimeSpan.FromMinutes(2), reason);
                    return false;
                }
                return true;
            }

            bool CheckMessageRateLimit(string remoteEndpoint)
            {
                string reason;
                var mps = AddMessagesAndGetRate(1);
                if (_ipMaxMessagesPerSecond > 0)
                {
                    var ipRate = _ipRates.GetOrAdd(remoteEndpoint, _ => new IpSlidingRate());
                    var ipMpsNow = ipRate.AddMessagesAndGetRate(1);
                    if (ipMpsNow > _ipMaxMessagesPerSecond)
                    {
                        reason = "ip_message_rate_limit_exceeded";
                        Log($"[IP限速-频率] {remoteEndpoint} {ipMpsNow}/s > {_ipMaxMessagesPerSecond}/s，断开");
                        Interlocked.Increment(ref _rateEventCount);
                        AddToBlacklist(remoteEndpoint, TimeSpan.FromMinutes(2), reason);
                        return false;
                    }
                }
                if (mps > _maxMessagesPerSecond)
                {
                    reason = "message_rate_limit_exceeded";
                    Log($"限速超限(频率): {remoteEndpoint} {mps}/s > {_maxMessagesPerSecond}/s，断开");
                    Interlocked.Increment(ref _rateEventCount);
                    AddToBlacklist(remoteEndpoint, TimeSpan.FromMinutes(2), reason);
                    return false;
                }
                return true;
            }

            var subject = new Subject<Envelope>();
            var sub = subject.Subscribe(async void (envelope) =>
            {
                try
                {
                    var incomingReqId = string.IsNullOrWhiteSpace(envelope.RequestId)
                        ? Guid.NewGuid().ToString("N")
                        : envelope.RequestId;
                    var reqContext = new RequestServerContext(this, envelope.Token ?? string.Empty, remote, incomingReqId);
                    if (_typedHandlers.TryGetValue(envelope.Type, out var strong))
                    {
                        var result = await strong.Invoke(envelope.Payload, reqContext);
                        if (result != null)
                        {
                            await WriteEnvelopeAsync(stream, result.Value.responseType, result.Value.payload,
                                envelope.RequestId, result.Value.errorCode, result.Value.errorMessage, token,
                                _receiveTimeoutMs);
                        }
                    }
                    else
                    {
                        Log($"未知消息类型: {envelope.Type}");
                        CoreTelemetry.RecordUnknown(ServerName, envelope.Type);
                    }
                }
                catch (Exception e)
                {
                    Log(e.Message);
                }
            });

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var lenBuf = new byte[4];
                    var read = await ReadExactAsync(stream, lenBuf, 0, 4, token, _receiveTimeoutMs);
                    if (read == 0) break;
                    if (!CheckByteRateLimit(read, remote)) break;
                    var length = BitConverter.ToInt32(lenBuf, 0);
                    if (length <= 0 || length > _maxMessageSize)
                    {
                        Log($"非法或过大消息长度: {length}，断开连接");
                        break;
                    }
                    var buf = new byte[length];
                    await ReadExactAsync(stream, buf, 0, length, token, _receiveTimeoutMs);
                    if (!CheckByteRateLimit(length, remote)) break;
                    Envelope envelope;
                    try
                    {
                        envelope = Envelope.Parser.ParseFrom(buf);
                    }
                    catch (InvalidProtocolBufferException exception)
                    {
                        Log($"协议解析异常(Envelope): {exception.Message}，断开");
                        break;
                    }
                    if (!CheckMessageRateLimit(remote)) break;
                    subject.OnNext(envelope);
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

            try { subject.OnCompleted(); }
            catch
            {
                // 结束事件流时的异常可忽略
            }
            try { sub.Dispose(); }
            catch
            {
                // 释放订阅时的异常可忽略
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
            if (DateTime.UtcNow < until) return true;
            _blacklist.TryRemove(remote, out _);
        }

        return false;
    }

    private void AddToBlacklist(string remote, TimeSpan duration, string reason)
    {
        _blacklist[remote] = DateTime.UtcNow.Add(duration);
        Log($"加入黑名单: {remote} 原因={reason} 时长={duration}");
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

/// <summary>
/// 程序集内密封类，用于封装请求上下文信息
/// </summary>
internal class RequestServerContext : IServerContext
{
    private readonly BaseGameServer _server;
    private readonly string _token;
    private AuthInfo? _auth;
    private readonly string _requestId;

    public RequestServerContext(BaseGameServer server, string? token, string remote, string requestId)
    {
        _server = server;
        _token = token ?? string.Empty;
        RemoteAddress = remote;
        _requestId = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId;
    }

    public string ServerName => _server.ServerName;
    public string AccessToken => _token;
    public string RemoteAddress { get; }
    public string RequestId => _requestId;
    public AuthInfo? Auth => _auth;

    public void Log(string message) => _server.Log(message);

    public void Log(string message, LogLevel level) => _server.Log(message, level);
    public T? GetComponent<T>() where T : class, IServerComponent => _server.GetComponent<T>();

    /// <summary>
    /// 内部：设置鉴权后的身份信息。
    /// </summary>
    internal void SetAuth(AuthInfo info)
    {
        _auth = info;
    }
}
    
