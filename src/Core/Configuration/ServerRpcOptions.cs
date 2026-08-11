namespace Core.Configuration;

/// <summary>服务器内部 RPC 的连接、超时、重试和鉴权配置。</summary>
public sealed class ServerRpcOptions
{
    public string SourceServerId { get; set; } = string.Empty;
    public string TokenSecret { get; set; } = string.Empty;
    public string DefaultRegion { get; set; } = string.Empty;
    public string DefaultStrategy { get; set; } = "load";
    public int ConnectTimeoutMs { get; set; } = 3_000;
    public int RequestTimeoutMs { get; set; } = 5_000;
    public int MaxFrameSizeBytes { get; set; } = 4 * 1024 * 1024;
    public int MaxAttempts { get; set; } = 2;
    public int TokenTtlSeconds { get; set; } = 30;
    public bool CheckCertificateRevocation { get; set; } = true;
    public int FailureThreshold { get; set; } = 3;
    public int FailureCooldownSeconds { get; set; } = 10;
    public int MaxPooledConnections { get; set; } = 256;
    public int IdleConnectionTimeoutSeconds { get; set; } = 60;
    public int PoolCleanupIntervalSeconds { get; set; } = 15;
}
