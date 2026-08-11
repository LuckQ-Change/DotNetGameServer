using Core;

namespace Hosting;

/// <summary>
/// 持有当前进程内运行的服务器实例，供健康检查与管理命令访问。
/// 拆分为单服务器进程后通常只有一个实例。
/// </summary>
public sealed class CurrentServerAccessor
{
    private BaseGameServer? _server;

    public BaseGameServer? Server => Volatile.Read(ref _server);

    public void Set(BaseGameServer? server) => Interlocked.Exchange(ref _server, server);

    public void Clear() => Interlocked.Exchange(ref _server, null);
}
