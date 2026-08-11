using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;

using Core.Client;
using Core.Components;
using Core.Configuration;
using Core.Logging;
using Core.MessagePipeline;
using Core.Telemetry;

using DotNetGameServer;

using Google.Protobuf;

using Microsoft.Extensions.Logging;

namespace Core;

public abstract class BaseGameServer : IServerContext
{
    private readonly int _maxMessageSize;
    private readonly IPAddress _listenAddress;
    private readonly bool _noDelay;
    private readonly int _maxConcurrentConnections;
    private readonly int _connectionQueueCapacity;
    private readonly int _maxConcurrentRequestsPerConnection;
    private readonly int _receiveTimeoutMs;
    private readonly int _acceptBackoffMs;
    private readonly int _requestTimeoutMs;
    private readonly int _startupTimeoutMs;
    private readonly int _shutdownTimeoutMs;
    private readonly int _listenBacklog;
    private readonly int _maxMessageTypeLength;
    private readonly int _maxRequestIdLength;
    private readonly int _maxTokenLength;
    private readonly int _maxVersionLength;
    private readonly bool _allowUnspecifiedChannel;
    private int _currentConnections;
    private readonly SemaphoreSlim _connectionSlots;
    private readonly ConcurrentDictionary<long, Task> _clientTasks = new();
    private long _nextClientTaskId;
    private TcpListener? _listener;
    private Task? _acceptTask;

    private CancellationTokenSource? _cts;

    // 请求处理管线：遥测 -> 鉴权 -> 处理器分发。
    private MessageDelegate? _pipeline;

    private readonly ILogger _logger;
    private readonly bool _forwardToILogger;

    // 连接数指标通过 Meter 注册，无需持有字段引用。
    private readonly ConcurrentDictionary<string, IHandlerInvoker> _typedHandlers = new();
    private readonly HashSet<CommunicationChannel> _allowedChannels;

    private readonly ConcurrentDictionary<Type, IServerComponent> _components = new();
    private readonly List<Type> _componentRegistrationOrder = [];
    private readonly object _componentGate = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly List<IServerComponent> _initializedComponents = [];

    // 网络鲁棒性：限速与黑名单
    private readonly int _maxBytesPerSecond; // 默认每连接 1MB/s
    private readonly int _maxMessagesPerSecond; // 默认每连接 200 条/秒
    private readonly int _idleTimeoutMs; // 默认空闲 120s 断开

    private readonly ConcurrentDictionary<string, DateTime> _blacklist = new();

    // 传输层安全配置
    private readonly TlsOptions _tlsOptions;
    private X509Certificate2? _serverCert;

    // 聚合的滑动窗口限流
    private readonly ConcurrentDictionary<string, IpSlidingRate> _ipRates = new();
    private readonly int _ipMaxBytesPerSecond; // 0 表示禁用
    private readonly int _ipMaxMessagesPerSecond; // 0 表示禁用

    private int _rateEventCount;
    private Task? _maintenanceTask;

    /// <summary>
    /// 服务器名称，用于日志前缀与标识。
    /// </summary>
    public string ServerName { get; }

    // 基类级默认上下文字段：用于非请求场景（初始化等）。
    public string AccessToken => string.Empty;
    public string RemoteAddress => "";
    public string RequestId => "";
    public AuthInfo? Auth => null;
    public CancellationToken CancellationToken => _cts?.Token ?? CancellationToken.None;

    /// <summary>
    /// 当前监听端口；未启动或已停止时为 <c>null</c>。
    /// </summary>
    public int? ListeningPort { get; private set; }
    public bool IsRunning => _listener != null;
    public string TokenAudience { get; }
    public int TokenClockSkewSeconds { get; }
    public bool RequireClientAuthentication { get; }
    public bool RequireInternalAuthentication { get; }

    protected BaseGameServer(
        string serverName,
        ILogger? logger,
        ServerOptions? options = null,
        AuthenticationOptions? authentication = null,
        bool forwardToILogger = false,
        IEnumerable<CommunicationChannel>? allowedChannels = null,
        ClientHeartbeatOptions? clientHeartbeat = null)
    {
        ServerName = serverName;
        _logger = logger ?? new DelegateLogger(Console.WriteLine, serverName);
        _forwardToILogger = forwardToILogger || LogCenter.ForwardToILogger;
        var settings = options ?? new ServerOptions();
        if (!IPAddress.TryParse(settings.ListenAddress, out var listenAddress))
            throw new ArgumentException($"监听地址不是有效 IP: {settings.ListenAddress}", nameof(options));
        _listenAddress = listenAddress;
        _noDelay = settings.NoDelay;
        _maxMessageSize = Math.Max(1024, settings.MaxMessageSizeBytes);
        _maxConcurrentConnections = Math.Max(1, settings.MaxConcurrentConnections);
        _connectionQueueCapacity = Math.Max(1, settings.ConnectionQueueCapacity);
        _maxConcurrentRequestsPerConnection = Math.Clamp(settings.MaxConcurrentRequestsPerConnection, 1, 64);
        _connectionSlots = new SemaphoreSlim(_maxConcurrentConnections, _maxConcurrentConnections);
        _receiveTimeoutMs = Math.Max(0, settings.ReceiveTimeoutMs);
        _acceptBackoffMs = Math.Max(0, settings.AcceptBackoffMs);
        _requestTimeoutMs = Math.Max(0, settings.RequestTimeoutMs);
        _startupTimeoutMs = Math.Max(1_000, settings.StartupTimeoutMs);
        _shutdownTimeoutMs = Math.Max(1_000, settings.ShutdownTimeoutMs);
        _listenBacklog = Math.Max(1, settings.ListenBacklog);
        _maxMessageTypeLength = Math.Max(1, settings.MaxMessageTypeLength);
        _maxRequestIdLength = Math.Max(1, settings.MaxRequestIdLength);
        _maxTokenLength = Math.Max(1, settings.MaxTokenLength);
        _maxVersionLength = Math.Max(1, settings.MaxVersionLength);
        _allowUnspecifiedChannel = settings.AllowUnspecifiedChannel;
        _maxBytesPerSecond = Math.Max(1, settings.MaxBytesPerSecond);
        _maxMessagesPerSecond = Math.Max(1, settings.MaxMessagesPerSecond);
        _idleTimeoutMs = Math.Max(0, settings.IdleTimeoutMs);
        _ipMaxBytesPerSecond = Math.Max(0, settings.IpMaxBytesPerSecond);
        _ipMaxMessagesPerSecond = Math.Max(0, settings.IpMaxMessagesPerSecond);
        _tlsOptions = settings.Tls ?? new TlsOptions();
        _allowedChannels = new HashSet<CommunicationChannel>(
            allowedChannels ?? [CommunicationChannel.Client]);
        if (_allowedChannels.Count == 0)
            throw new ArgumentException("至少需要允许一个通信通道。", nameof(allowedChannels));
        var authSettings = authentication ?? new AuthenticationOptions();
        TokenAudience = string.IsNullOrWhiteSpace(authSettings.Audience) ? serverName : authSettings.Audience;
        TokenClockSkewSeconds = Math.Max(0, authSettings.ClockSkewSeconds);
        RequireClientAuthentication = authSettings.RequireClientAuthentication;
        RequireInternalAuthentication = authSettings.RequireInternalAuthentication;

        try
        {
            AddComponent<ITokenSecretProvider>(new TokenSecretProviderComponent(authSettings.TokenSecret));
            AddComponent<ITokenRevocationStore>(new InMemoryTokenRevocationStore());
        }
        catch (Exception ex)
        {
            Log($"初始化 TokenSecretProviderComponent 失败: {ex.Message}");
        }

        if (clientHeartbeat?.Enabled == true)
            RegisterClientMessageHandler(new ClientHeartbeatHandler(clientHeartbeat));
    }

    public void AddComponent<T>(T component) where T : class, IServerComponent
    {
        ArgumentNullException.ThrowIfNull(component);
        lock (_componentGate)
        {
            if (_cts is not null)
                throw new InvalidOperationException("服务器启动后不能再修改组件集合。");
            var key = typeof(T);
            if (_components.TryAdd(key, component))
                _componentRegistrationOrder.Add(key);
            else
                _components[key] = component;
        }
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
    /// 路由仅使用 Protobuf 完整类型名，避免不同协议包中的同名消息互相覆盖。
    /// </summary>
    /// <typeparam name="TReq">请求消息类型。</typeparam>
    /// <typeparam name="TResp">响应消息类型。</typeparam>
    /// <param name="handler">强类型消息处理器。</param>
    protected void RegisterClientMessageHandler<TReq, TResp>(IMessageHandler<TReq, TResp> handler)
        where TReq : class, IMessage<TReq>, new()
        where TResp : class, IMessage<TResp>, new()
    {
        RegisterMessageHandler(handler, CommunicationChannel.Client);
    }

    /// <summary>注册仅允许服务器内部通道调用的强类型消息处理器。</summary>
    protected void RegisterInternalMessageHandler<TReq, TResp>(IMessageHandler<TReq, TResp> handler)
        where TReq : class, IMessage<TReq>, new()
        where TResp : class, IMessage<TResp>, new() =>
        RegisterMessageHandler(handler, CommunicationChannel.Internal);

    private void RegisterMessageHandler<TReq, TResp>(
        IMessageHandler<TReq, TResp> handler,
        CommunicationChannel channel)
        where TReq : class, IMessage<TReq>, new()
        where TResp : class, IMessage<TResp>, new()
    {
        ArgumentNullException.ThrowIfNull(handler);
        var invoker = new HandlerInvoker<TReq, TResp>(handler);
        var requestFullName = new TReq().Descriptor.FullName;
        if (!_typedHandlers.TryAdd(BuildHandlerKey(channel, requestFullName), invoker))
            throw new InvalidOperationException($"消息处理器重复注册: {channel}/{requestFullName}");
    }

    /// <summary>
    /// 启动服务器监听并初始化所有已注册组件。
    /// </summary>
    /// <param name="port">监听端口。</param>
    public virtual Task StartAsync(int port) => StartAsync(port, CancellationToken.None);

    public virtual async Task StartAsync(int port, CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            await StartCoreAsync(port, cancellationToken);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task StartCoreAsync(int port, CancellationToken cancellationToken)
    {
        if (_cts is not null) return;
        if (port is < 0 or > IPEndPoint.MaxPort)
            throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be between 0 and 65535.");

        _cts = new CancellationTokenSource();
        using var startupCts = CancellationTokenSource.CreateLinkedTokenSource(
            _cts.Token,
            cancellationToken);
        startupCts.CancelAfter(_startupTimeoutMs);
        try
        {
            // 逐个初始化组件，确保依赖就绪
            IServerComponent[] components;
            lock (_componentGate)
                components = _componentRegistrationOrder.Select(key => _components[key]).ToArray();
            foreach (var component in components)
            {
                await component.InitializeAsync(startupCts.Token).WaitAsync(startupCts.Token);
                _initializedComponents.Add(component);
            }

            // TLS 证书加载（仅 Enabled=true 时执行）
            if (_tlsOptions.Enabled)
            {
                if (string.IsNullOrWhiteSpace(_tlsOptions.CertificatePath))
                    throw new InvalidOperationException("TLS 已启用但未配置 CertificatePath。");
                const X509KeyStorageFlags certificateFlags = X509KeyStorageFlags.MachineKeySet;
                _serverCert = string.IsNullOrWhiteSpace(_tlsOptions.CertificatePassword)
                    ? new X509Certificate2(_tlsOptions.CertificatePath, (string?)null, certificateFlags)
                    : new X509Certificate2(
                        _tlsOptions.CertificatePath,
                        _tlsOptions.CertificatePassword,
                        certificateFlags);
                Log($"TLS 已启用，证书: {_serverCert.Subject}，指纹: {_serverCert.Thumbprint}");
            }

            _listener = new TcpListener(_listenAddress, port);
            _listener.Start(_listenBacklog);
            ListeningPort = (_listener.LocalEndpoint as IPEndPoint)?.Port ?? port;

            // 构建请求处理管线（健康检查端点由宿主统一提供，基类不再内嵌 Web 服务器）。
            var pipelineBuilder = new MessagePipelineBuilder()
                .UseChannelValidation()
                .UseTelemetry()
                .UseAuth();
            ConfigureMessagePipeline(pipelineBuilder);
            _pipeline = pipelineBuilder.UseHandler().Build();

            Log($"服务器已启动，端口 {ListeningPort}");

            _acceptTask = AcceptLoop(_cts.Token);
            _maintenanceTask = RunMaintenanceLoopAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            _listener?.Stop();
            _listener = null;
            ListeningPort = null;
            _serverCert?.Dispose();
            _serverCert = null;
            using var rollbackCts = new CancellationTokenSource(_shutdownTimeoutMs);
            if (_cts is not null)
            {
                try
                {
                    await _cts.CancelAsync().WaitAsync(rollbackCts.Token);
                }
                catch (OperationCanceledException) when (rollbackCts.IsCancellationRequested)
                {
                    Log("启动回滚取消超时。", LogLevel.Warning);
                }
            }
            await DestroyInitializedComponentsAsync(rollbackCts.Token);
            _cts?.Dispose();
            _cts = null;
            throw new InvalidOperationException($"Failed to start server '{ServerName}'.", ex);
        }
    }

    /// <summary>
    /// 停止服务器监听并销毁所有组件。
    /// </summary>
    public Task StopAsync() => StopAsync(CancellationToken.None);

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync(cancellationToken);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        var cts = _cts;
        if (cts is null) return;

        using var shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        shutdownCts.CancelAfter(_shutdownTimeoutMs);
        var shutdownToken = shutdownCts.Token;

        try
        {
            await cts.CancelAsync().WaitAsync(shutdownToken);
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
            if (_acceptTask != null)
                await _acceptTask.WaitAsync(shutdownToken);
        }
        catch (OperationCanceledException)
        {
            // 正常关停。
        }
        catch (Exception ex)
        {
            Log($"等待接受连接任务结束失败: {ex.Message}");
        }

        var activeClients = _clientTasks.Values.ToArray();
        if (activeClients.Length > 0)
        {
            try
            {
                await Task.WhenAll(activeClients).WaitAsync(shutdownToken);
            }
            catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
            {
                Log($"等待客户端连接关闭超时，剩余任务数: {_clientTasks.Count}", LogLevel.Warning);
            }
            catch (Exception ex)
            {
                Log($"等待客户端连接关闭失败: {ex.Message}", LogLevel.Warning);
            }
        }

        _listener = null;
        _acceptTask = null;
        ListeningPort = null;
        _pipeline = null;
        _serverCert?.Dispose();
        _serverCert = null;

        try
        {
            if (_maintenanceTask is not null)
                await _maintenanceTask.WaitAsync(shutdownToken);
        }
        catch (OperationCanceledException)
        {
            // 正常关闭。
        }
        _maintenanceTask = null;

        await DestroyInitializedComponentsAsync(shutdownToken);

        cts.Dispose();
        _cts = null;

        Log("服务器已停止");
    }

    private async Task DestroyInitializedComponentsAsync(CancellationToken cancellationToken)
    {
        for (var index = _initializedComponents.Count - 1; index >= 0; index--)
        {
            var component = _initializedComponents[index];
            try
            {
                await component.DestroyAsync(cancellationToken).WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Log($"组件 {component.Name} 释放超时。", LogLevel.Error);
            }
            catch (Exception ex)
            {
                Log($"组件 {component.Name} 释放失败: {ex.Message}", LogLevel.Error);
            }
        }
        _initializedComponents.Clear();
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

    /// <summary>
    /// 在鉴权之后、业务处理器之前添加应用中间件。
    /// </summary>
    /// <param name="pipeline">当前服务器的消息管线构建器。</param>
    protected virtual void ConfigureMessagePipeline(MessagePipelineBuilder pipeline)
    {
    }

    private async Task AcceptLoop(CancellationToken token)
    {
        var currentBackoffMs = _acceptBackoffMs;
        while (!token.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(token);
                client.NoDelay = _noDelay;
                // 黑名单：立即拒绝（按 IP 匹配，与限速加黑的键保持一致）
                var remoteEp = client.Client.RemoteEndPoint?.ToString() ?? "未知";
                var remoteIpAccept = (client.Client.RemoteEndPoint as IPEndPoint)?.Address?.ToString() ?? remoteEp;
                if (IsBlacklisted(remoteIpAccept))
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

                // 使用信号量原子占用连接槽，避免“检查后再计数”的突发连接竞态。
                if (!await _connectionSlots.WaitAsync(0, token))
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

                Interlocked.Increment(ref _currentConnections);
                CoreTelemetry.RecordConnectionDelta(ServerName, 1);
                var taskId = Interlocked.Increment(ref _nextClientTaskId);
                var clientTask = HandleClient(client, token);
                _clientTasks[taskId] = clientTask;
                _ = clientTask.ContinueWith(
                    completedTask => _clientTasks.TryRemove(taskId, out var removedTask),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
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
        int errorCode, string? errorMessage, CommunicationChannel channel, CancellationToken token, int timeoutMs,
        int maximumFrameBytes)
    {
        var env = new envelope { Type = type, Payload = ByteString.CopyFrom(payload) };
        if (!string.IsNullOrWhiteSpace(requestId)) env.RequestId = requestId;
        env.Channel = channel;
        // 服务器不再回写协议版本字段，简化并避免客户端版本依赖
        env.ErrorCode = errorCode;
        if (!string.IsNullOrWhiteSpace(errorMessage)) env.ErrorMessage = errorMessage;
        var bytes = env.ToByteArray();
        if (bytes.Length > maximumFrameBytes)
        {
            env.Payload = ByteString.Empty;
            env.ErrorCode = ProtocolError.ResponseTooLarge;
            env.ErrorMessage = "response_too_large";
            bytes = env.ToByteArray();
        }
        var lenBuf = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(lenBuf, bytes.Length);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (timeoutMs > 0) cts.CancelAfter(timeoutMs);
        await stream.WriteAsync(lenBuf.AsMemory(0, 4), cts.Token);
        await stream.WriteAsync(bytes.AsMemory(), cts.Token);
        await stream.FlushAsync(cts.Token);
    }

    private async Task HandleClient(TcpClient client, CancellationToken token)
    {
        using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var connectionToken = connectionCts.Token;
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "未知";
        var remoteIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address?.ToString() ?? remote;
        Log($"客户端连接: {remote}", LogLevel.Debug);
        try
        {
            using (client)
            {
                Stream stream = client.GetStream();

                // TLS 握手：在读写任何业务数据之前完成，握手失败立即断开
                if (_tlsOptions.Enabled && _serverCert != null)
                {
                    var sslStream = new SslStream(stream, leaveInnerStreamOpen: false);
                    try
                    {
                        using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(connectionToken);
                        handshakeCts.CancelAfter(Math.Max(1_000, _tlsOptions.HandshakeTimeoutMs));
                        await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                        {
                            ServerCertificate = _serverCert,
                            ClientCertificateRequired = _tlsOptions.RequireClientCertificate,
                            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                            CertificateRevocationCheckMode = _tlsOptions.CheckClientCertificateRevocation
                                ? X509RevocationMode.Online
                                : X509RevocationMode.NoCheck
                        }, handshakeCts.Token);
                        stream = sslStream;
                        Log($"TLS 握手成功: {remote} 协议={sslStream.SslProtocol}", LogLevel.Debug);
                    }
                    catch (Exception ex)
                    {
                        Log($"TLS 握手失败，断开: {remote} {ex.Message}", LogLevel.Warning);
                        await sslStream.DisposeAsync();
                        return;
                    }
                }

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

                var requestChannel = Channel.CreateBounded<envelope>(new BoundedChannelOptions(_connectionQueueCapacity)
                {
                    AllowSynchronousContinuations = false,
                    SingleReader = _maxConcurrentRequestsPerConnection == 1,
                    SingleWriter = true,
                    FullMode = BoundedChannelFullMode.Wait
                });
                using var responseWriteGate = new SemaphoreSlim(1, 1);

                async Task WriteResponseAsync(
                    string type,
                    byte[] payload,
                    string? requestId,
                    int errorCode,
                    string? errorMessage,
                    CommunicationChannel channel)
                {
                    await responseWriteGate.WaitAsync(connectionToken);
                    try
                    {
                        await WriteEnvelopeAsync(
                            stream,
                            type,
                            payload,
                            requestId,
                            errorCode,
                            errorMessage,
                            channel,
                            connectionToken,
                            _receiveTimeoutMs,
                            _maxMessageSize);
                    }
                    finally
                    {
                        responseWriteGate.Release();
                    }
                }

                async Task ProcessRequestsAsync()
                {
                    await foreach (var env in requestChannel.Reader.ReadAllAsync(connectionToken))
                    {
                        try
                        {
                            var incomingReqId = string.IsNullOrWhiteSpace(env.RequestId)
                                ? Guid.NewGuid().ToString("N")
                                : env.RequestId;
                            Log($"recv route={env.Type} req_id={incomingReqId} ip={remote}", LogLevel.Debug);

                            if (!TryValidateEnvelopeMetadata(env, out var metadataError))
                            {
                                await WriteResponseAsync(
                                    string.IsNullOrWhiteSpace(env.Type) ? "unknown" : env.Type[..Math.Min(env.Type.Length, _maxMessageTypeLength)],
                                    [],
                                    env.RequestId[..Math.Min(env.RequestId.Length, _maxRequestIdLength)],
                                    ProtocolError.InvalidRequest,
                                    metadataError,
                                    env.Channel);
                                continue;
                            }

                            using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(connectionToken);
                            if (_requestTimeoutMs > 0) requestCts.CancelAfter(_requestTimeoutMs);
                            var msgContext = new MessageContext
                            {
                                Envelope = env,
                                Server = this,
                                RemoteAddress = remote,
                                RequestId = incomingReqId,
                                Token = requestCts.Token
                            };

                            try
                            {
                                var pipelineTask = _pipeline?.Invoke(msgContext) ?? Task.CompletedTask;
                                await pipelineTask.WaitAsync(requestCts.Token);
                            }
                            catch (OperationCanceledException) when (!connectionToken.IsCancellationRequested && requestCts.IsCancellationRequested)
                            {
                                msgContext.ResponseType = string.IsNullOrWhiteSpace(env.Type) ? "unknown" : env.Type;
                                msgContext.ResponsePayload = [];
                                msgContext.ResponseErrorCode = ProtocolError.RequestTimeout;
                                msgContext.ResponseErrorMessage = "request_timeout";
                            }

                            if (msgContext.HasResponse)
                            {
                                if ((msgContext.ResponsePayload?.Length ?? 0) > _maxMessageSize)
                                {
                                    msgContext.ResponsePayload = [];
                                    msgContext.ResponseErrorCode = ProtocolError.ResponseTooLarge;
                                    msgContext.ResponseErrorMessage = "response_too_large";
                                }
                                await WriteResponseAsync(
                                    msgContext.ResponseType!,
                                    msgContext.ResponsePayload ?? [],
                                    incomingReqId,
                                    msgContext.ResponseErrorCode,
                                    msgContext.ResponseErrorMessage,
                                    msgContext.EffectiveChannel);
                                Log($"send route={msgContext.ResponseType} req_id={incomingReqId} ip={remote} error_code={msgContext.ResponseErrorCode}", LogLevel.Debug);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception e)
                        {
                            Log($"请求处理异常: {e}", LogLevel.Error);
                        }
                    }
                }

                var requestProcessors = Enumerable
                    .Range(0, _maxConcurrentRequestsPerConnection)
                    .Select(_ => Task.Run(ProcessRequestsAsync, connectionToken))
                    .ToArray();

                try
                {
                    while (!connectionToken.IsCancellationRequested)
                    {
                        var lenBuf = new byte[4];
                        var read = await ReadExactAsync(stream, lenBuf, 0, 4, connectionToken, _idleTimeoutMs);
                        if (read == 0) break;
                        if (!CheckByteRateLimit(read, remote, remoteIp)) break;
                        var length = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
                        if (length <= 0 || length > _maxMessageSize)
                        {
                            Log($"非法或过大消息长度: {length}，断开连接");
                            break;
                        }
                        var buf = new byte[length];
                        var payloadRead = await ReadExactAsync(stream, buf, 0, length, connectionToken, _receiveTimeoutMs);
                        if (payloadRead != length)
                        {
                            Log($"消息体未完整读取: expected={length} actual={payloadRead}，断开连接");
                            break;
                        }
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
                        await requestChannel.Writer.WriteAsync(envelope, connectionToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    Log($"客户端请求被取消: {remote}", LogLevel.Debug);
                }
                catch (IOException ioException)
                {
                    Log($"客户端网络IO断开: {remote} {ioException.Message}", LogLevel.Debug);
                }
                catch (SocketException sex)
                {
                    Log($"客户端套接字断开: {remote} {sex.Message}", LogLevel.Debug);
                }
                catch (Exception ex)
                {
                    Log($"客户端异常: {ex}", LogLevel.Error);
                }

                requestChannel.Writer.TryComplete();
                try
                {
                    await connectionCts.CancelAsync()
                        .WaitAsync(TimeSpan.FromMilliseconds(_shutdownTimeoutMs));
                }
                catch (TimeoutException)
                {
                    Log($"取消连接请求处理超时: {remote}", LogLevel.Warning);
                }
                try { await Task.WhenAll(requestProcessors); }
                catch (OperationCanceledException) when (connectionToken.IsCancellationRequested)
                {
                    // 连接关闭或服务器停止时的正常取消。
                }
                catch (Exception ex)
                {
                    Log($"请求处理器异常: {ex.Message}");
                }
                Log($"客户端断开: {remote}", LogLevel.Debug);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _currentConnections);
            CoreTelemetry.RecordConnectionDelta(ServerName, -1);
            _connectionSlots.Release();
        }
    }

    private bool TryValidateEnvelopeMetadata(envelope message, out string error)
    {
        if (string.IsNullOrWhiteSpace(message.Type) || message.Type.Length > _maxMessageTypeLength)
            error = "invalid_message_type";
        else if (message.RequestId.Length > _maxRequestIdLength)
            error = "invalid_request_id";
        else if (message.Token.Length > _maxTokenLength)
            error = "invalid_token_length";
        else if (message.Version.Length > _maxVersionLength)
            error = "invalid_version_length";
        else
        {
            error = string.Empty;
            return true;
        }
        return false;
    }

    private void CleanupExpiredNetworkState()
    {
        var now = DateTime.UtcNow;
        foreach (var item in _blacklist)
        {
            if (item.Value <= now)
                _blacklist.TryRemove(item.Key, out _);
        }

        // IP 滑动窗口只保存最近一秒的数据；超过两分钟未活动即可安全移除。
        foreach (var item in _ipRates)
        {
            if (item.Value.LastActivityUtc <= now.AddMinutes(-2))
                _ipRates.TryRemove(item.Key, out _);
        }
    }

    private async Task RunMaintenanceLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var nextCleanup = DateTime.UtcNow.AddMinutes(1);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var rateEvents = Interlocked.Exchange(ref _rateEventCount, 0);
            if (rateEvents > 0) Log($"限流事件: {rateEvents}", LogLevel.Warning);
            if (DateTime.UtcNow < nextCleanup) continue;
            CleanupExpiredNetworkState();
            nextCleanup = DateTime.UtcNow.AddMinutes(1);
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

    internal bool TryGetHandler(
        CommunicationChannel channel,
        string messageType,
        out IHandlerInvoker? invoker) =>
        _typedHandlers.TryGetValue(BuildHandlerKey(channel, messageType), out invoker);

    internal bool TryResolveChannel(CommunicationChannel requested, out CommunicationChannel resolved)
    {
        resolved = requested;
        if (requested == CommunicationChannel.ChannelUnspecified)
        {
            if (!_allowUnspecifiedChannel)
                return false;
            resolved = CommunicationChannel.Client;
        }
        return _allowedChannels.Contains(resolved);
    }

    private static string BuildHandlerKey(CommunicationChannel channel, string messageType) =>
        $"{(int)channel}:{messageType}";

    public ServerRuntimeSnapshot GetRuntimeSnapshot()
    {
        var load = GetComponent<IServerLoadProvider>();
        return new ServerRuntimeSnapshot(
            ServerName,
            ListeningPort,
            IsRunning,
            Volatile.Read(ref _currentConnections),
            load?.PlayerCount ?? 0,
            load?.Capacity ?? 0);
    }

    public async Task<IReadOnlyList<ServerComponentHealth>> GetComponentHealthAsync(
        CancellationToken cancellationToken = default)
    {
        var checks = _components.Values.OfType<IServerComponentHealth>().ToArray();
        if (checks.Length == 0) return [];
        var results = await Task.WhenAll(checks.Select(check => check.CheckHealthAsync(cancellationToken)));
        return results;
    }

    /// <summary>
    /// IP 滑动窗口限流器（线程安全：可被多个连接的读取任务并发访问）
    /// </summary>
    private sealed class IpSlidingRate
    {
        private const int BucketCount = 10;
        private const int BucketMs = 100;
        private int _currentBucketIndex;
        private long _bucketWindowStartTicks = Stopwatch.GetTimestamp();
        private readonly int[] _messageBuckets = new int[BucketCount];
        private readonly int[] _byteBuckets = new int[BucketCount];
        private readonly object _lock = new();
        private long _lastActivityUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public DateTime LastActivityUtc => DateTimeOffset.FromUnixTimeSeconds(
            Interlocked.Read(ref _lastActivityUnixSeconds)).UtcDateTime;

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

        public int AddBytesAndGetRate(int bytes)
        {
            lock (_lock)
            {
                Interlocked.Exchange(ref _lastActivityUnixSeconds, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                AdvanceBucketsIfNeeded();
                _byteBuckets[_currentBucketIndex] += bytes;
                var sum = 0;
                for (var i = 0; i < BucketCount; i++) sum += _byteBuckets[i];
                return sum;
            }
        }

        public int AddMessagesAndGetRate(int inc)
        {
            lock (_lock)
            {
                Interlocked.Exchange(ref _lastActivityUnixSeconds, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                AdvanceBucketsIfNeeded();
                _messageBuckets[_currentBucketIndex] += inc;
                var sum = 0;
                for (var i = 0; i < BucketCount; i++) sum += _messageBuckets[i];
                return sum;
            }
        }
    }
}

