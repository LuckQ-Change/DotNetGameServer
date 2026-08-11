namespace Core;

public sealed record ServerComponentHealth(string Name, bool Healthy, string Message);

/// <summary>需要参与 readiness 判断的组件实现此接口。</summary>
public interface IServerComponentHealth
{
    Task<ServerComponentHealth> CheckHealthAsync(CancellationToken cancellationToken = default);
}
