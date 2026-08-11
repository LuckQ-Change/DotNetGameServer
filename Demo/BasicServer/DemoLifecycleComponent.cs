using Core;

namespace BasicServer;

/// <summary>跟随服务器生命周期初始化和释放资源的最小示例。</summary>
public sealed class DemoLifecycleComponent : IServerComponent
{
    public string Name => nameof(DemoLifecycleComponent);

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public Task DestroyAsync()
    {
        return Task.CompletedTask;
    }
}
