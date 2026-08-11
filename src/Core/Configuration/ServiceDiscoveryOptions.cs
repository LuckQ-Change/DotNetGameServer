namespace Core.Configuration;

/// <summary>与存储实现无关的服务注册生命周期配置。</summary>
public sealed class ServiceDiscoveryOptions
{
    public int OfflineTtlSeconds { get; set; } = 120;
    public int RemoveTtlSeconds { get; set; } = 600;
    public int CleanupIntervalSeconds { get; set; } = 10;
}
