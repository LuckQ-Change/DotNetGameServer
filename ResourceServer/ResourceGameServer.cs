using Core;

namespace ResourceServer;

/// <summary>
/// 资源服务器：负责版本与AB获取（演示）
/// </summary>
public class ResourceGameServer : BaseGameServer
{
    public ResourceGameServer(Action<string> log, string? resourceRoot = null) : base("Resource", log)
    {
        RegisterMessageHandler(new QueryVersionMessageHandler());
        RegisterMessageHandler(new GetBundleMessageHandler());

        var root = resourceRoot ?? Path.Combine(AppContext.BaseDirectory, "Resources");
        AddComponent<IResourceRepositoryComponent>(new FileSystemResourceRepository(root));
        Log($"资源根目录: {root}");
    }
}