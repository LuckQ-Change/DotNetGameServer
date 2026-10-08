namespace Core;

/// <summary>供健康检查和宿主集成使用的只读运行状态。</summary>
public sealed record ServerRuntimeSnapshot(
    string ServerName,
    int? ListeningPort,
    bool IsRunning,
    int CurrentConnections,
    int PlayerCount,
    int Capacity)
{
    public double Load => Capacity > 0 ? (double)PlayerCount / Capacity : 0d;
}
