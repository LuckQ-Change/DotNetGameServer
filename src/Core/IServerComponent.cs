using DotNetGameServer;
using Microsoft.Extensions.Logging;

namespace Core;

/// <summary>
/// 服务器组件接口，用于ECS架构中的组件系统
/// </summary>
public interface IServerComponent
{
    /// <summary>
    /// 组件名称，用于日志标识与诊断。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 初始化组件，准备运行期资源与依赖。
    /// </summary>
    /// <returns>表示异步完成的任务。</returns>
    Task InitializeAsync();

    /// <summary>
    /// 销毁组件，释放运行期资源。
    /// </summary>
    /// <returns>表示异步完成的任务。</returns>
    Task DestroyAsync();
}

/// <summary>
/// 消息处理器接口
/// </summary>


/// <summary>
/// 服务器上下文接口
/// </summary>
public interface IServerContext
{
    /// <summary>
    /// 服务器名称，用于区分日志来源与实例。
    /// </summary>
    string ServerName { get; }

    /// <summary>
    /// 请求级访问令牌（来自 Envelope.token），无则为空字符串。
    /// </summary>
    string AccessToken { get; }

    /// <summary>
    /// 请求ID（服务器生成），用于结构化日志关联。
    /// </summary>
    string RequestId { get; }

    /// <summary>
    /// 远端地址（客户端连接地址），便于审计与监控。
    /// </summary>
    string RemoteAddress { get; }

    /// <summary>
    /// 请求级身份信息（若令牌可校验则填充）。
    /// </summary>
    AuthInfo? Auth { get; }

    /// <summary>
    /// 写入日志消息，默认为 Information 级别。
    /// </summary>
    /// <param name="message">日志内容。</param>
    void Log(string message);

    /// <summary>
    /// 写入指定级别的日志消息。
    /// </summary>
    /// <param name="message">日志内容。</param>
    /// <param name="level">日志级别。</param>
    void Log(string message, LogLevel level);

    /// <summary>
    /// 获取指定类型的服务器组件。
    /// </summary>
    /// <typeparam name="T">组件类型。</typeparam>
    /// <returns>若存在则返回组件实例；否则返回 <c>null</c>。</returns>
    T? GetComponent<T>() where T : class, IServerComponent;
}