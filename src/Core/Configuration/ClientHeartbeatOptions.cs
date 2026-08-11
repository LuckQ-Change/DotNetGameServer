namespace Core.Configuration;

/// <summary>客户端应用层心跳配置；默认关闭，由需要维持客户端会话的服务器显式启用。</summary>
public sealed class ClientHeartbeatOptions
{
    public bool Enabled { get; set; }
    public int RecommendedIntervalSeconds { get; set; } = 20;
}
