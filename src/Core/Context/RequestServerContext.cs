using Microsoft.Extensions.Logging;

namespace Core;

/// <summary>
/// 程序集内密封类，用于封装请求上下文信息
/// </summary>
internal class RequestServerContext : IServerContext
{
    private readonly BaseGameServer _server;
    private readonly string _token;
    private AuthInfo? _auth;
    private readonly string _requestId;

    public RequestServerContext(
        BaseGameServer server,
        string? token,
        string remote,
        string requestId,
        string connectionId,
        CancellationToken cancellationToken)
    {
        _server = server;
        _token = token ?? string.Empty;
        RemoteAddress = remote;
        _requestId = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId;
        ConnectionId = connectionId ?? string.Empty;
        CancellationToken = cancellationToken;
    }

    public string ServerName => _server.ServerName;
    public string AccessToken => _token;
    public string RemoteAddress { get; }
    public string RequestId => _requestId;
    public string ConnectionId { get; }
    public string TokenAudience => _server.TokenAudience;
    public int ClientTokenTtlSeconds => _server.ClientTokenTtlSeconds;
    public int ClientTokenRefreshGraceSeconds => _server.ClientTokenRefreshGraceSeconds;
    public AuthInfo? Auth => _auth;
    public CancellationToken CancellationToken { get; }

    public void Log(string message) =>
        _server.LogWithContext(message, ServerName, LogLevel.Information, RemoteAddress, RequestId, _auth?.UserId);

    public void Log(string message, LogLevel level) =>
        _server.LogWithContext(message, ServerName, level, RemoteAddress, RequestId, _auth?.UserId);
    public T? GetComponent<T>() where T : class, IServerComponent => _server.GetComponent<T>();

    /// <summary>
    /// 内部：设置鉴权后的身份信息。
    /// </summary>
    internal void SetAuth(AuthInfo info)
    {
        _auth = info;
    }
}
