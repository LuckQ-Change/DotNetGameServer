using DotNetGameServer;

namespace Core;

/// <summary>
/// Protobuf 响应类的 IServerResponse 适配器
/// 将现有的 Protobuf 响应类型桥接为 IServerResponse 接口
/// </summary>
public static class ProtobufResponseExtensions
{
    /// <summary>
    /// 将 HeartbeatResponse 转换为 IServerResponse
    /// 修复：消除反射依赖，提供强类型接口访问
    /// </summary>
    public static IServerResponse AsServerResponse(this HeartbeatResponse response)
    {
        return new ProtobufServerResponseAdapter<HeartbeatResponse>(
            response,
            r => r.Accepted,
            r => r.Accepted ? 0 : 401,
            r => r.Message);
    }

    public static IServerResponse AsServerResponse(this ServerQueryResponse response)
    {
        return new ProtobufServerResponseAdapter<ServerQueryResponse>(
            response,
            r => true,
            r => 0,
            r => string.Empty);
    }
}

internal sealed class ProtobufServerResponseAdapter<T> : IServerResponse
{
    private readonly T _response;
    private readonly Func<T, bool> _successGetter;
    private readonly Func<T, int> _codeGetter;
    private readonly Func<T, string> _messageGetter;

    public ProtobufServerResponseAdapter(
        T response,
        Func<T, bool> successGetter,
        Func<T, int> codeGetter,
        Func<T, string> messageGetter)
    {
        _response = response;
        _successGetter = successGetter;
        _codeGetter = codeGetter;
        _messageGetter = messageGetter;
    }

    public bool Success => _successGetter(_response);
    public int Code => _codeGetter(_response);
    public string Message => _messageGetter(_response);
}