using Google.Protobuf;

namespace Core.Rpc;

/// <summary>可供服务器内部调用的目标节点。</summary>
public sealed record ServerEndpoint(
    string ServerId,
    string ServerType,
    string Host,
    int Port,
    string TokenAudience,
    bool TlsEnabled = false,
    string? TlsServerName = null);

/// <summary>服务节点解析接口，可对接框架发现协议或业务自定义注册中心。</summary>
public interface IServiceResolver
{
    Task<IReadOnlyList<ServerEndpoint>> ResolveAsync(
        string serverType,
        string? region,
        string? strategy,
        string? routingKey,
        int limit,
        CancellationToken cancellationToken = default);
}

/// <summary>服务器内部类型安全调用接口。</summary>
public interface IServerRpcClient
{
    Task<TResponse> CallAsync<TRequest, TResponse>(
        string serverType,
        TRequest request,
        ServerCallOptions? callOptions = null,
        CancellationToken cancellationToken = default)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new();
}

public sealed class ServerCallOptions
{
    public string? Region { get; init; }
    public string? Strategy { get; init; }
    public string? RoutingKey { get; init; }
    public int? TimeoutMs { get; init; }

    /// <summary>只有明确标记为幂等的调用才允许在网络故障后自动重试。</summary>
    public bool Idempotent { get; init; }
    public int? MaxAttempts { get; init; }
}

public sealed class ServerRpcException : Exception
{
    public ServerRpcException(
        string message,
        int errorCode = 500,
        string? remoteError = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        RemoteError = remoteError;
    }

    public int ErrorCode { get; }
    public string? RemoteError { get; }
}
