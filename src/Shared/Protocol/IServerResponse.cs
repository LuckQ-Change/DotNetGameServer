namespace Shared;

/// <summary>
/// 服务器响应契约，用于消除反射依赖
/// </summary>
public interface IServerResponse
{
    /// <summary>
    /// 请求是否成功
    /// </summary>
    bool Success { get; }

    /// <summary>
    /// 响应代码（0 表示成功）
    /// </summary>
    int Code { get; }

    /// <summary>
    /// 响应消息
    /// </summary>
    string Message { get; }
}
