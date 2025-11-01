using DotNetGameServer;

namespace Core;

/// <summary>
/// 服务器组件接口，用于ECS架构中的组件系统
/// </summary>
public interface IServerComponent
{
    /// <summary>
    /// 组件名称
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// 初始化组件
    /// </summary>
    Task InitializeAsync();
    
    /// <summary>
    /// 销毁组件
    /// </summary>
    Task DestroyAsync();
}

/// <summary>
/// 消息处理器接口
/// </summary>
public interface IMessageHandler
{
    /// <summary>
    /// 支持的消息类型
    /// </summary>
    string MessageType { get; }
    
    /// <summary>
    /// 处理消息
    /// </summary>
    Task<byte[]?> HandleAsync(byte[] payload, IServerContext context);
}

/// <summary>
/// 服务器上下文接口
/// </summary>
public interface IServerContext
{
    /// <summary>
    /// 服务器名称
    /// </summary>
    string ServerName { get; }
    
    /// <summary>
    /// 日志记录
    /// </summary>
    void Log(string message);
    
    /// <summary>
    /// 获取组件
    /// </summary>
    T? GetComponent<T>() where T : class, IServerComponent;
}