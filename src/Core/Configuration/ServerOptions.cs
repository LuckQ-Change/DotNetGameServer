namespace Core.Configuration;

public class ServerOptions
{
    /// <summary>监听地址；使用 0.0.0.0 或 :: 才会对外网卡开放。</summary>
    public string ListenAddress { get; set; } = "127.0.0.1";
    public bool NoDelay { get; set; } = true;
    public int MaxMessageSizeBytes { get; set; } = 1 * 1024 * 1024;
    public int MaxConcurrentConnections { get; set; } = 200;
    public int ReceiveTimeoutMs { get; set; } = 10000;
    public int AcceptBackoffMs { get; set; } = 200;
    public int MaxBytesPerSecond { get; set; } = 1_000_000;
    public int MaxMessagesPerSecond { get; set; } = 200;
    public int IdleTimeoutMs { get; set; } = 120_000;
    /// <summary>每连接允许积压的待处理请求数；达到上限后暂停网络读取以形成背压。</summary>
    public int ConnectionQueueCapacity { get; set; } = 256;
    /// <summary>单条连接可并行处理的请求数；响应通过写锁保持帧完整。</summary>
    public int MaxConcurrentRequestsPerConnection { get; set; } = 4;
    /// <summary>单个请求处理的最长时间；0 表示不设置业务处理截止时间。</summary>
    public int RequestTimeoutMs { get; set; } = 30_000;
    /// <summary>组件初始化和监听器启动的最长时间。</summary>
    public int StartupTimeoutMs { get; set; } = 30_000;
    /// <summary>宿主等待连接和组件优雅退出的最长时间。</summary>
    public int ShutdownTimeoutMs { get; set; } = 15_000;
    /// <summary>TCP listen backlog。</summary>
    public int ListenBacklog { get; set; } = 512;
    public int MaxMessageTypeLength { get; set; } = 512;
    public int MaxRequestIdLength { get; set; } = 128;
    public int MaxTokenLength { get; set; } = 8_192;
    public int MaxVersionLength { get; set; } = 64;
    public int IpMaxBytesPerSecond { get; set; }
    public int IpMaxMessagesPerSecond { get; set; }

    /// <summary>是否兼容未声明通信通道的旧客户端；默认关闭以避免协议边界混淆。</summary>
    public bool AllowUnspecifiedChannel { get; set; }

    /// <summary>TCP 层 TLS 配置；Enabled=false 时使用明文传输。</summary>
    public TlsOptions Tls { get; set; } = new();
}
