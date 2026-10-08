using System.Collections.Concurrent;

using DotNetGameServer;

using Google.Protobuf;

namespace Core.MessagePipeline;

/// <summary>
/// 保存消息处理器路由，并统一执行通信通道解析。
/// </summary>
/// <remarks>
/// 路由键由“通信通道 + Protobuf 完整类型名”组成，既隔离客户端/内部调用，
/// 也避免不同 Protobuf 包中同名消息相互覆盖。
/// </remarks>
internal sealed class MessageHandlerRegistry
{
    private readonly ConcurrentDictionary<string, IHandlerInvoker> _handlers = new();
    private readonly HashSet<CommunicationChannel> _allowedChannels;
    private readonly bool _allowUnspecifiedChannel;
    private readonly object _gate = new();

    public MessageHandlerRegistry(
        IEnumerable<CommunicationChannel>? allowedChannels,
        bool allowUnspecifiedChannel)
    {
        _allowedChannels = new HashSet<CommunicationChannel>(
            allowedChannels ?? [CommunicationChannel.Client]);
        if (_allowedChannels.Count == 0)
            throw new ArgumentException("至少需要允许一个通信通道。", nameof(allowedChannels));

        _allowUnspecifiedChannel = allowUnspecifiedChannel;
    }

    /// <summary>注册一个指定通信通道的强类型消息处理器。</summary>
    public void Register<TRequest, TResponse>(
        IMessageHandler<TRequest, TResponse> handler,
        CommunicationChannel channel)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
    {
        ArgumentNullException.ThrowIfNull(handler);
        var invoker = new HandlerInvoker<TRequest, TResponse>(handler);
        var messageType = new TRequest().Descriptor.FullName;
        lock (_gate)
        {
            if (!_handlers.TryAdd(BuildKey(channel, messageType), invoker))
                throw new InvalidOperationException($"消息处理器重复注册: {channel}/{messageType}");
        }
    }

    /// <summary>替换已有处理器。进行中的请求仍会走被替换的实例，新请求立刻走新实例。</summary>
    public HandlerReplacement Replace<TRequest, TResponse>(
        IMessageHandler<TRequest, TResponse> handler,
        CommunicationChannel channel)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
    {
        ArgumentNullException.ThrowIfNull(handler);
        var invoker = new HandlerInvoker<TRequest, TResponse>(handler);
        var messageType = new TRequest().Descriptor.FullName;
        lock (_gate)
        {
            var key = BuildKey(channel, messageType);
            _handlers.TryGetValue(key, out var previous);
            _handlers[key] = invoker;
            return new HandlerReplacement(invoker, previous);
        }
    }

    public bool TryLease(
        CommunicationChannel channel,
        string messageType,
        out HandlerLease lease)
    {
        lock (_gate)
        {
            if (!_handlers.TryGetValue(BuildKey(channel, messageType), out var invoker) || invoker is null)
            {
                lease = default;
                return false;
            }

            invoker.Enter();
            lease = new HandlerLease(invoker);
            return true;
        }
    }

    public bool TryGet(
        CommunicationChannel channel,
        string messageType,
        out IHandlerInvoker? invoker) =>
        _handlers.TryGetValue(BuildKey(channel, messageType), out invoker);

    /// <summary>
    /// 将协议中的通道值解析为服务器实际接受的通道。
    /// 未声明通道只在显式开启旧客户端兼容时回退到客户端通道。
    /// </summary>
    public bool TryResolveChannel(CommunicationChannel requested, out CommunicationChannel resolved)
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

    private static string BuildKey(CommunicationChannel channel, string messageType) =>
        $"{(int)channel}:{messageType}";
}

internal readonly struct HandlerLease : IDisposable
{
    private readonly IHandlerInvoker? _invoker;

    public HandlerLease(IHandlerInvoker invoker) => _invoker = invoker;

    public IHandlerInvoker Invoker => _invoker ?? throw new InvalidOperationException("处理器租约无效。");

    public void Dispose() => _invoker?.Release();
}

internal readonly struct HandlerReplacement
{
    public HandlerReplacement(IHandlerInvoker current, IHandlerInvoker? previous)
    {
        Current = current;
        Previous = previous;
    }

    public IHandlerInvoker Current { get; }

    public IHandlerInvoker? Previous { get; }
}
