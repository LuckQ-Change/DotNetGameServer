namespace Core.Configuration;

/// <summary>应用服务器向服务发现节点注册时使用的配置。</summary>
public sealed class ServiceRegistrationOptions
{
    public string Region { get; set; } = "default";
    public int Capacity { get; set; } = 100;
    public int IntervalSeconds { get; set; } = 5;
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; }
    public string ServerType { get; set; } = "default";
    public string ServerId { get; set; } = string.Empty;
    public string DiscoveryHost { get; set; } = "127.0.0.1";
    public int DiscoveryPort { get; set; } = 5500;
    public string DiscoveryAudience { get; set; } = "ServiceDiscoveryServer";
    public bool DiscoveryTlsEnabled { get; set; }
    public string DiscoveryTlsServerName { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string TokenAudience { get; set; } = string.Empty;
    public bool TlsEnabled { get; set; }
    public string TlsServerName { get; set; } = string.Empty;
}
