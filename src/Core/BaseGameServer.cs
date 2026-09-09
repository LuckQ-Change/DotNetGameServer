using System.Collections.Concurrent;
using System.Net;

using Core.Client;
using Core.Components;
using Core.Configuration;
using Core.Logging;
using Core.MessagePipeline;
using Core.Networking;

using DotNetGameServer;

using Google.Protobuf;

using Microsoft.Extensions.Logging;

namespace Core;

/// <summary>
/// 游戏服务器的公共基类，负责组合组件、消息管线和 TCP 传输层。
/// </summary>
/// <remarks>
/// 派生类通常只需要注册业务组件与强类型消息处理器，不需要接触套接字、
/// TLS、限流或协议帧等底层细节。具体网络职责位于 <see cref="Core.Networking"/>。
/// </remarks>
public abstract class BaseGameServer : IServerContext
{
    // 对外可写的 ServerOptions 在构造时转换为不可变快照，防止运行期间配置漂移。
    private readonly ServerRuntimeOptions _options;
    private readonly MessageHandlerRegistry _handlerRegistry;
    private readonly ILogger _logger;
    private readonly bool _forwardToILogger;

    // 组件按注册顺序初始化、按相反顺序销毁。
    private readonly ConcurrentDictionary<Type, IServerComponent> _components = new();
    private readonly List<Type> _componentRegistrationOrder = [];
    private readonly List<IServerComponent> _initializedComponents = [];
    private readonly object _componentGate = new();

    // 生命周期门保证并发 Start/Stop 不会重复创建或释放同一批资源。
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private CancellationTokenSource? _cts;
    private TcpServerTransport? _transport;

    /// <summary>服务器名称，用于日志分类、遥测标签和运行实例标识。</summary>
    public string ServerName { get; }

    /// <summary>
    /// 基类本身不对应具体请求，因此访问令牌为空；业务请求会使用
    /// <see cref="RequestServerContext"/> 提供请求级上下文。
    /// </summary>
    public string AccessToken => string.Empty;

    public string RemoteAddress => string.Empty;

    public string RequestId => string.Empty;

    public AuthInfo? Auth => null;

    /// <summary>服务器运行令牌；服务器尚未启动时返回不可取消令牌。</summary>
    public CancellationToken CancellationToken => _cts?.Token ?? CancellationToken.None;

    /// <summary>当前监听端口；未启动或已停止时为 <c>null</c>。</summary>
    public int? ListeningPort { get; private set; }

    /// <summary>传输层是否已经成功绑定并正在监听。</summary>
    public bool IsRunning => _transport?.IsRunning == true;

    /// <summary>令牌校验期望的受众。</summary>
    public string TokenAudience { get; }

    /// <summary>令牌时间校验允许的时钟偏差秒数。</summary>
    public int TokenClockSkewSeconds { get; }

    /// <summary>客户端通信通道是否强制鉴权。</summary>
    public bool RequireClientAuthentication { get; }

    /// <summary>服务器内部通信通道是否强制鉴权。</summary>
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
        _options = ServerRuntimeOptions.Create(options, nameof(options));
        _handlerRegistry = new MessageHandlerRegistry(
            allowedChannels,
            _options.AllowUnspecifiedChannel);

        var authSettings = authentication ?? new AuthenticationOptions();
        TokenAudience = string.IsNullOrWhiteSpace(authSettings.Audience)
            ? serverName
            : authSettings.Audience;
        TokenClockSkewSeconds = Math.Max(0, authSettings.ClockSkewSeconds);
        RequireClientAuthentication = authSettings.RequireClientAuthentication;
        RequireInternalAuthentication = authSettings.RequireInternalAuthentication;

        // 基础鉴权组件始终存在，业务服务器可通过相同接口替换默认实现。
        AddComponent<ITokenSecretProvider>(new TokenSecretProviderComponent(authSettings.TokenSecret));
        AddComponent<ITokenRevocationStore>(new InMemoryTokenRevocationStore());

        if (clientHeartbeat?.Enabled == true)
            RegisterClientMessageHandler(new ClientHeartbeatHandler(clientHeartbeat));
    }

    /// <summary>
    /// 注册或替换一个服务器组件。相同服务类型再次注册时替换实例，但保留原初始化顺序。
    /// </summary>
    /// <exception cref="InvalidOperationException">服务器启动后不允许改变组件集合。</exception>
    public void AddComponent<T>(T component) where T : class, IServerComponent
    {
        ArgumentNullException.ThrowIfNull(component);
        lock (_componentGate)
        {
            if (_cts is not null)
                throw new InvalidOperationException("服务器启动后不能再修改组件集合。");

            var componentType = typeof(T);
            if (_components.TryAdd(componentType, component))
                _componentRegistrationOrder.Add(componentType);
            else
                _components[componentType] = component;
        }
    }

    /// <summary>按注册时使用的接口或具体类型获取服务器组件。</summary>
    public T? GetComponent<T>() where T : class, IServerComponent =>
        _components.TryGetValue(typeof(T), out var component) ? component as T : null;

    /// <summary>
    /// 注册允许普通游戏客户端调用的强类型消息处理器。
    /// 路由使用 Protobuf 完整类型名，避免不同协议包中的同名消息相互覆盖。
    /// </summary>
    protected void RegisterClientMessageHandler<TRequest, TResponse>(
        IMessageHandler<TRequest, TResponse> handler)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new() =>
        _handlerRegistry.Register(handler, CommunicationChannel.Client);

    /// <summary>注册只允许可信服务器内部通道调用的强类型消息处理器。</summary>
    protected void RegisterInternalMessageHandler<TRequest, TResponse>(
        IMessageHandler<TRequest, TResponse> handler)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new() =>
        _handlerRegistry.Register(handler, CommunicationChannel.Internal);

    /// <summary>使用不可取消令牌启动服务器。</summary>
    public virtual Task StartAsync(int port) => StartAsync(port, CancellationToken.None);

    /// <summary>
    /// 初始化组件、构建消息管线并绑定 TCP 端口。并发调用只会完成一次实际启动。
    /// </summary>
    /// <param name="port">监听端口；传入 0 时由操作系统分配可用端口。</param>
    /// <param name="cancellationToken">取消启动等待的令牌。</param>
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
        startupCts.CancelAfter(_options.StartupTimeoutMs);

        try
        {
            await InitializeComponentsAsync(startupCts.Token);

            // 固定顺序保证通道边界和鉴权一定早于业务处理器执行。
            var pipelineBuilder = new MessagePipelineBuilder()
                .UseChannelValidation()
                .UseTelemetry()
                .UseAuth();
            ConfigureMessagePipeline(pipelineBuilder);
            var pipeline = pipelineBuilder.UseHandler().Build();

            _transport = new TcpServerTransport(this, _options, pipeline);
            _transport.Start(port, _cts.Token);
            ListeningPort = _transport.ListeningPort;
            Log($"服务器已启动，端口 {ListeningPort}");
        }
        catch (Exception exception)
        {
            await RollbackFailedStartAsync();
            throw new InvalidOperationException($"Failed to start server '{ServerName}'.", exception);
        }
    }

    /// <summary>使用不可取消令牌优雅停止服务器。</summary>
    public Task StopAsync() => StopAsync(CancellationToken.None);

    /// <summary>
    /// 停止接入、取消活动请求并按反向顺序销毁组件。并发调用只执行一次实际停止。
    /// </summary>
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
        var serverCts = _cts;
        if (serverCts is null) return;

        using var shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        shutdownCts.CancelAfter(_options.ShutdownTimeoutMs);
        var shutdownToken = shutdownCts.Token;

        try
        {
            await serverCts.CancelAsync().WaitAsync(shutdownToken);
        }
        catch (Exception exception)
        {
            // 即使取消令牌异常，也继续释放监听器和已初始化组件。
            Log($"取消服务器运行令牌失败: {exception.Message}");
        }

        if (_transport is not null)
        {
            await _transport.StopAsync(shutdownToken);
            _transport = null;
        }

        ListeningPort = null;
        await DestroyInitializedComponentsAsync(shutdownToken);

        serverCts.Dispose();
        _cts = null;
        Log("服务器已停止");
    }

    private async Task InitializeComponentsAsync(CancellationToken cancellationToken)
    {
        IServerComponent[] components;
        lock (_componentGate)
            components = _componentRegistrationOrder.Select(type => _components[type]).ToArray();

        foreach (var component in components)
        {
            await component.InitializeAsync(cancellationToken).WaitAsync(cancellationToken);
            _initializedComponents.Add(component);
        }
    }

    /// <summary>启动失败时撤销已经创建的传输资源和已初始化组件。</summary>
    private async Task RollbackFailedStartAsync()
    {
        _transport?.StopAccepting();
        ListeningPort = null;
        using var rollbackCts = new CancellationTokenSource(_options.ShutdownTimeoutMs);

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

        if (_transport is not null)
        {
            await _transport.StopAsync(rollbackCts.Token);
            _transport = null;
        }

        await DestroyInitializedComponentsAsync(rollbackCts.Token);
        _cts?.Dispose();
        _cts = null;
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
            catch (Exception exception)
            {
                Log($"组件 {component.Name} 释放失败: {exception.Message}", LogLevel.Error);
            }
        }

        _initializedComponents.Clear();
    }

    /// <summary>写入 Information 级别日志，并自动附加服务器名称。</summary>
    public void Log(string message) =>
        LogWithCategory(message, ServerName, LogLevel.Information);

    /// <summary>写入指定级别日志，并自动附加服务器名称。</summary>
    public void Log(string message, LogLevel level) =>
        LogWithCategory(message, ServerName, level);

    /// <summary>写入心跳分类日志。</summary>
    public void LogHeartbeat(string message) =>
        LogWithCategory(message, "Heartbeat", LogLevel.Information);

    /// <summary>写入显式分类的日志。</summary>
    public void LogWithCategory(string message, string category, LogLevel level) =>
        LogWithContext(message, category, level);

    /// <summary>发布带远端地址、请求 ID 和用户 ID 的结构化日志。</summary>
    internal void LogWithContext(
        string message,
        string category,
        LogLevel level,
        string? remote = null,
        string? requestId = null,
        string? userId = null)
    {
        if (!LogCenter.ShouldLog(level, category)) return;

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

    /// <summary>
    /// 在鉴权之后、业务处理器之前添加应用中间件。
    /// </summary>
    protected virtual void ConfigureMessagePipeline(MessagePipelineBuilder pipeline)
    {
    }

    internal bool TryGetHandler(
        CommunicationChannel channel,
        string messageType,
        out IHandlerInvoker? invoker) =>
        _handlerRegistry.TryGet(channel, messageType, out invoker);

    internal bool TryResolveChannel(
        CommunicationChannel requested,
        out CommunicationChannel resolved) =>
        _handlerRegistry.TryResolveChannel(requested, out resolved);

    /// <summary>获取供健康检查和服务发现使用的轻量运行快照。</summary>
    public ServerRuntimeSnapshot GetRuntimeSnapshot()
    {
        var load = GetComponent<IServerLoadProvider>();
        return new ServerRuntimeSnapshot(
            ServerName,
            ListeningPort,
            IsRunning,
            _transport?.CurrentConnections ?? 0,
            load?.PlayerCount ?? 0,
            load?.Capacity ?? 0);
    }

    /// <summary>并行执行所有实现了健康检查接口的服务器组件。</summary>
    public async Task<IReadOnlyList<ServerComponentHealth>> GetComponentHealthAsync(
        CancellationToken cancellationToken = default)
    {
        var checks = _components.Values.OfType<IServerComponentHealth>().ToArray();
        if (checks.Length == 0) return [];
        return await Task.WhenAll(checks.Select(check => check.CheckHealthAsync(cancellationToken)));
    }
}
