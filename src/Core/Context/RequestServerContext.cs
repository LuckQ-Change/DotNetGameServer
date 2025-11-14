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
