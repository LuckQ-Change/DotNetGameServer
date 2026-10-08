using Google.Protobuf;

namespace Core.Plugins;

/// <summary>可在服务器运行期间装入的逻辑插件。入口程序集本身不能卸载。</summary>
public interface IServerPlugin
{
    /// <summary>插件名称，写入更新结果。</summary>
    public string Name { get; }

    /// <summary>声明要替换的消息处理器。此时连接和监听端口保持不变。</summary>
    public void Configure(IPluginCatalog catalog);
}

/// <summary>插件用来登记要热替换的处理器。</summary>
public interface IPluginCatalog
{
    /// <summary>替换客户端通道上的处理器。类型第一次出现时等价于新增。</summary>
    public void MapClient<TRequest, TResponse>(IMessageHandler<TRequest, TResponse> handler)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new();

    /// <summary>替换内部通道上的处理器。</summary>
    public void MapInternal<TRequest, TResponse>(IMessageHandler<TRequest, TResponse> handler)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new();
}

/// <summary>一次插件装载的结果。装载返回时新处理器已经生效。</summary>
public sealed class PluginLoadResult
{
    public PluginLoadResult(string pluginName, bool previousPluginReleased)
    {
        PluginName = pluginName;
        PreviousPluginReleased = previousPluginReleased;
    }

    /// <summary>本次装入的插件名称。</summary>
    public string PluginName { get; }

    /// <summary>上一份插件的进行中请求已经结束，并且其程序集已标记卸载。</summary>
    public bool PreviousPluginReleased { get; }
}
