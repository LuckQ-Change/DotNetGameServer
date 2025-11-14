using Microsoft.Extensions.Logging;

namespace Core;

/// <summary>
/// 服务器上下文接口
/// </summary>
public interface IServerContext
{
    /// <summary>
    /// 服务器名称，用于区分日志来源与实例。
    /// </summary>
    public string ServerName { get; }

    /// <summary>
    /// 请求级访问令牌（来自 Envelope.token），无则为空字符串。
    /// </summary>
    public string AccessToken { get; }

    /// <summary>
    /// 请求ID（服务器生成），用于结构化日志关联。
    /// </summary>
    public string RequestId { get; }

    /// <summary>
    /// 远端地址（客户端连接地址），便于审计与监控。
    /// </summary>
    public string RemoteAddress { get; }

    /// <summary>
    /// 请求级身份信息（若令牌可校验则填充）。
    /// </summary>
    public AuthInfo? Auth { get; }

    /// <summary>
    /// 写入日志消息，默认为 Information 级别。
    /// </summary>
    /// <param name="message">日志内容。</param>
    public void Log(string message);

    /// <summary>
    /// 写入指定级别的日志消息。
    /// </summary>
    /// <param name="message">日志内容。</param>
    /// <param name="level">日志级别。</param>
    public void Log(string message, LogLevel level);

    /// <summary>
    /// 获取指定类型的服务器组件。
    /// </summary>
    /// <typeparam name="T">组件类型。</typeparam>
    /// <returns>若存在则返回组件实例；否则返回 <c>null</c>。</returns>
    public T? GetComponent<T>() where T : class, IServerComponent;
}
