using System.Collections.Concurrent;

using DotNetGameServer;

namespace Core.Networking;

/// <summary>一条已完成传输握手的客户端连接，供请求处理和主动推送共用写锁。</summary>
internal sealed class ClientSession
{
    private CommunicationChannel _channel = CommunicationChannel.Client;

    public ClientSession(
        string connectionId,
        string remoteAddress,
        Action requestClose,
        Func<string, byte[], CommunicationChannel, CancellationToken, ValueTask> sendAsync)
    {
        ConnectionId = connectionId;
        RemoteAddress = remoteAddress;
        RequestClose = requestClose;
        SendAsync = sendAsync;
    }

    public string ConnectionId { get; }

    public string RemoteAddress { get; }

    public Action RequestClose { get; }

    public Func<string, byte[], CommunicationChannel, CancellationToken, ValueTask> SendAsync { get; }

    public CommunicationChannel Channel => _channel;

    public void ObserveChannel(CommunicationChannel channel)
    {
        if (channel is CommunicationChannel.Client or CommunicationChannel.Internal)
            _channel = channel;
    }
}

/// <summary>进程内的活动连接表。连接关闭后条目会被移除。</summary>
internal sealed class ClientSessionRegistry
{
    private readonly ConcurrentDictionary<string, ClientSession> _sessions = new(StringComparer.Ordinal);

    public void Add(ClientSession session) => _sessions[session.ConnectionId] = session;

    public void Remove(string connectionId) => _sessions.TryRemove(connectionId, out _);

    public bool TryDisconnect(string connectionId)
    {
        if (!_sessions.TryGetValue(connectionId, out var session)) return false;
        session.RequestClose();
        return true;
    }

    public bool TryGet(string connectionId, out ClientSession? session) =>
        _sessions.TryGetValue(connectionId, out session);

    public ClientSession[] Snapshot() => _sessions.Values.ToArray();
}
